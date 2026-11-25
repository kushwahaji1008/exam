using MongoDB.Driver;
using ProctoringService.Models;

namespace ProctoringService.Services
{
    public interface IProctoringAttemptSuspender
    {
        Task SuspendAttemptAsync(string attemptId, string reason);
    }

    public class ProctoringManagementService
    {
        private const int MaxSnapshotHistory = 50;
        private readonly MongoDbService _mongoDb;
        private readonly AIAnalysisService _aiAnalysis;
        private readonly ILogger<ProctoringManagementService> _logger;
        private readonly IProctoringAttemptSuspender? _attemptSuspender;

        public ProctoringManagementService(
            MongoDbService mongoDb,
            AIAnalysisService aiAnalysis,
            ILogger<ProctoringManagementService> logger,
            IProctoringAttemptSuspender? attemptSuspender = null)
        {
            _mongoDb = mongoDb;
            _aiAnalysis = aiAnalysis;
            _logger = logger;
            _attemptSuspender = attemptSuspender;
        }

        public async Task<ProctoringSession> StartSessionAsync(string attemptId, string examId, string studentId)
        {
            var existingSession = await _mongoDb.ProctoringSessions
                .Find(s => s.AttemptId == attemptId && s.Status == SessionStatus.Active)
                .FirstOrDefaultAsync();

            if (existingSession != null)
            {
                return existingSession;
            }

            var session = new ProctoringSession
            {
                AttemptId = attemptId,
                ExamId = examId,
                StudentId = studentId,
                StartedAt = DateTime.UtcNow,
                Status = SessionStatus.Active
            };

            await _mongoDb.ProctoringSessions.InsertOneAsync(session);
            return session;
        }

        public async Task<ProctoringSession?> GetSessionAsync(string sessionId)
        {
            return await _mongoDb.ProctoringSessions.Find(s => s.Id == sessionId).FirstOrDefaultAsync();
        }

        public async Task<ProctoringSession?> GetActiveSessionByAttemptAsync(string attemptId)
        {
            return await _mongoDb.ProctoringSessions
                .Find(s => s.AttemptId == attemptId && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Flagged))
                .FirstOrDefaultAsync();
        }

        public async Task<List<ProctoringSession>> GetSessionsByExamAsync(string examId)
        {
            return await _mongoDb.ProctoringSessions
                .Find(s => s.ExamId == examId)
                .SortByDescending(s => s.StartedAt)
                .ToListAsync();
        }

        public async Task<bool> ReportViolationAsync(string sessionId, Violation violation)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null) return false;

            // Compute risk score INCLUDING this new violation before persisting
            var simulatedSession = new ProctoringSession
            {
                TabSwitchCount = session.TabSwitchCount + (violation.Type is ViolationType.TabSwitch or ViolationType.WindowSwitch ? 1 : 0),
                MultipleFacesDetected = session.MultipleFacesDetected + (violation.Type == ViolationType.MultipleFaces ? 1 : 0),
                NoFaceDetectedCount = session.NoFaceDetectedCount + (violation.Type == ViolationType.NoFaceDetected ? 1 : 0),
                SuspiciousMovementCount = session.SuspiciousMovementCount + (violation.Type is ViolationType.SuspiciousMovement or ViolationType.LookingAway ? 1 : 0),
                AudioAnomaliesCount = session.AudioAnomaliesCount + (violation.Type == ViolationType.AudioAnomaly ? 1 : 0),
                Violations = session.Violations.Concat(new[] { violation }).ToList()
            };

            var riskScore = CalculateRiskScore(simulatedSession);
            var riskLevel = GetRiskLevel(riskScore);

            var update = Builders<ProctoringSession>.Update
                .Push(s => s.Violations, violation)
                .Inc(s => s.TotalViolations, 1)
                .Set(s => s.RiskScore, riskScore)
                .Set(s => s.RiskLevel, riskLevel);

            switch (violation.Type)
            {
                case ViolationType.TabSwitch:
                case ViolationType.WindowSwitch:
                    update = update.Inc(s => s.TabSwitchCount, 1);
                    break;
                case ViolationType.MultipleFaces:
                    update = update.Inc(s => s.MultipleFacesDetected, 1);
                    break;
                case ViolationType.NoFaceDetected:
                    update = update.Inc(s => s.NoFaceDetectedCount, 1);
                    break;
                case ViolationType.SuspiciousMovement:
                case ViolationType.LookingAway:
                    update = update.Inc(s => s.SuspiciousMovementCount, 1);
                    break;
                case ViolationType.AudioAnomaly:
                    update = update.Inc(s => s.AudioAnomaliesCount, 1);
                    break;
            }

            if (riskLevel == RiskLevel.Critical && session.Status == SessionStatus.Active)
            {
                update = update.Set(s => s.Status, SessionStatus.Flagged);
            }

            var result = await _mongoDb.ProctoringSessions.UpdateOneAsync(s => s.Id == sessionId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> SubmitSnapshotAsync(string sessionId, Snapshot snapshot)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null) return false;
            if (snapshot.ImageBase64.Length > 5 * 1024 * 1024) return false;

            snapshot.Analysis = await _aiAnalysis.AnalyzeImageAsync(snapshot.ImageBase64);

            // Atomic capped append ($push + $each + $slice: -50) prevents concurrent snapshot loss
            var pushUpdate = Builders<ProctoringSession>.Update
                .PushEach(s => s.Snapshots, new[] { snapshot }, slice: -MaxSnapshotHistory);
            var pushResult = await _mongoDb.ProctoringSessions.UpdateOneAsync(s => s.Id == sessionId, pushUpdate);

            if (snapshot.Analysis != null)
            {
                if (snapshot.Analysis.MultipleFaces)
                    await ReportViolationAsync(sessionId, new Violation { Type = ViolationType.MultipleFaces, Description = "Multiple faces detected in frame", Severity = ViolationSeverity.High, SnapshotUrl = snapshot.Id });
                if (!snapshot.Analysis.FaceDetected)
                    await ReportViolationAsync(sessionId, new Violation { Type = ViolationType.NoFaceDetected, Description = "No face detected in frame", Severity = ViolationSeverity.Medium, SnapshotUrl = snapshot.Id });
                if (snapshot.Analysis.LookingAway)
                    await ReportViolationAsync(sessionId, new Violation { Type = ViolationType.LookingAway, Description = "Student looking away from screen", Severity = ViolationSeverity.Low, SnapshotUrl = snapshot.Id });
                if (snapshot.Analysis.PhoneDetected)
                    await ReportViolationAsync(sessionId, new Violation { Type = ViolationType.PhoneDetected, Description = "Mobile phone detected", Severity = ViolationSeverity.Critical, SnapshotUrl = snapshot.Id });
                if (snapshot.Analysis.BookDetected)
                    await ReportViolationAsync(sessionId, new Violation { Type = ViolationType.BookDetected, Description = "Books or notes detected", Severity = ViolationSeverity.High, SnapshotUrl = snapshot.Id });
            }

            return pushResult.MatchedCount > 0;
        }

        public async Task<bool> SetMonitoringStateAsync(string sessionId, bool isLiveMonitored, string? proctorId)
        {
            var update = Builders<ProctoringSession>.Update
                .Set(s => s.IsLiveMonitored, isLiveMonitored)
                .Set(s => s.MonitoredBy, isLiveMonitored ? proctorId : null);
            var result = await _mongoDb.ProctoringSessions.UpdateOneAsync(s => s.Id == sessionId, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> SuspendSessionAsync(string sessionId, string reason)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null) return false;

            var update = Builders<ProctoringSession>.Update
                .Set(s => s.Status, SessionStatus.Suspended)
                .Set(s => s.EndedAt, DateTime.UtcNow)
                .Push(s => s.Violations, new Violation
                {
                    Type = ViolationType.SuspiciousMovement,
                    Severity = ViolationSeverity.Critical,
                    Description = $"Session suspended by proctor: {reason}"
                });

            var result = await _mongoDb.ProctoringSessions.UpdateOneAsync(s => s.Id == sessionId, update);
            if (result.MatchedCount > 0 && _attemptSuspender != null && !string.IsNullOrEmpty(session.AttemptId))
            {
                await _attemptSuspender.SuspendAttemptAsync(session.AttemptId, reason);
            }
            return result.MatchedCount > 0;
        }

        public async Task<bool> EndSessionAsync(string sessionId)
        {
            var update = Builders<ProctoringSession>.Update
                .Set(s => s.EndedAt, DateTime.UtcNow)
                .Set(s => s.Status, SessionStatus.Completed);

            var result = await _mongoDb.ProctoringSessions.UpdateOneAsync(s => s.Id == sessionId, update);
            return result.MatchedCount > 0;
        }

        public static double CalculateRiskScore(ProctoringSession session)
        {
            double score = 0;
            score += session.TabSwitchCount * 2;
            score += session.MultipleFacesDetected * 10;
            score += session.NoFaceDetectedCount * 3;
            score += session.SuspiciousMovementCount * 1.5;
            score += session.AudioAnomaliesCount * 2;

            foreach (var violation in session.Violations)
            {
                score += violation.Severity switch
                {
                    ViolationSeverity.Critical => 20,
                    ViolationSeverity.High => 10,
                    ViolationSeverity.Medium => 5,
                    _ => 2
                };
            }

            return Math.Min(score, 100);
        }

        public static RiskLevel GetRiskLevel(double score)
        {
            if (score >= 70) return RiskLevel.Critical;
            if (score >= 40) return RiskLevel.High;
            if (score >= 20) return RiskLevel.Medium;
            return RiskLevel.Low;
        }
    }
}
