using MongoDB.Driver;
using ProctoringService.Models;

namespace ProctoringService.Services
{
    public class ProctoringManagementService
    {
        private readonly MongoDbService _mongoDb;
        private readonly AIAnalysisService _aiAnalysis;
        private readonly ILogger<ProctoringManagementService> _logger;

        public ProctoringManagementService(
            MongoDbService mongoDb,
            AIAnalysisService aiAnalysis,
            ILogger<ProctoringManagementService> logger)
        {
            _mongoDb = mongoDb;
            _aiAnalysis = aiAnalysis;
            _logger = logger;
        }

        public async Task<ProctoringSession> StartSessionAsync(string attemptId, string examId, string studentId)
        {
            // Check if session already exists
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
            return await _mongoDb.ProctoringSessions
                .Find(s => s.Id == sessionId)
                .FirstOrDefaultAsync();
        }

        public async Task<ProctoringSession?> GetActiveSessionByAttemptAsync(string attemptId)
        {
            return await _mongoDb.ProctoringSessions
                .Find(s => s.AttemptId == attemptId && s.Status == SessionStatus.Active)
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

            session.Violations.Add(violation);
            session.TotalViolations++;

            // Update specific counters
            switch (violation.Type)
            {
                case ViolationType.TabSwitch:
                case ViolationType.WindowSwitch:
                    session.TabSwitchCount++;
                    break;
                case ViolationType.MultipleFaces:
                    session.MultipleFacesDetected++;
                    break;
                case ViolationType.NoFaceDetected:
                    session.NoFaceDetectedCount++;
                    break;
                case ViolationType.SuspiciousMovement:
                case ViolationType.LookingAway:
                    session.SuspiciousMovementCount++;
                    break;
                case ViolationType.AudioAnomaly:
                    session.AudioAnomaliesCount++;
                    break;
            }

            // Calculate risk score
            session.RiskScore = CalculateRiskScore(session);
            session.RiskLevel = GetRiskLevel(session.RiskScore);

            // Auto-flag high-risk sessions
            if (session.RiskLevel == RiskLevel.Critical)
            {
                session.Status = SessionStatus.Flagged;
            }

            var update = Builders<ProctoringSession>.Update
                .Set(s => s.Violations, session.Violations)
                .Set(s => s.TotalViolations, session.TotalViolations)
                .Set(s => s.TabSwitchCount, session.TabSwitchCount)
                .Set(s => s.MultipleFacesDetected, session.MultipleFacesDetected)
                .Set(s => s.NoFaceDetectedCount, session.NoFaceDetectedCount)
                .Set(s => s.SuspiciousMovementCount, session.SuspiciousMovementCount)
                .Set(s => s.AudioAnomaliesCount, session.AudioAnomaliesCount)
                .Set(s => s.RiskScore, session.RiskScore)
                .Set(s => s.RiskLevel, session.RiskLevel)
                .Set(s => s.Status, session.Status);

            var result = await _mongoDb.ProctoringSessions.UpdateOneAsync(
                s => s.Id == sessionId,
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<bool> SubmitSnapshotAsync(string sessionId, Snapshot snapshot)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null) return false;

            // Perform AI analysis on snapshot
            snapshot.Analysis = await _aiAnalysis.AnalyzeImageAsync(snapshot.ImageBase64);

            // Check for violations based on AI analysis
            if (snapshot.Analysis != null)
            {
                if (snapshot.Analysis.MultipleFaces)
                {
                    await ReportViolationAsync(sessionId, new Violation
                    {
                        Type = ViolationType.MultipleFaces,
                        Description = "Multiple faces detected in frame",
                        Severity = ViolationSeverity.High,
                        SnapshotUrl = snapshot.Id
                    });
                }

                if (!snapshot.Analysis.FaceDetected)
                {
                    await ReportViolationAsync(sessionId, new Violation
                    {
                        Type = ViolationType.NoFaceDetected,
                        Description = "No face detected in frame",
                        Severity = ViolationSeverity.Medium,
                        SnapshotUrl = snapshot.Id
                    });
                }

                if (snapshot.Analysis.LookingAway)
                {
                    await ReportViolationAsync(sessionId, new Violation
                    {
                        Type = ViolationType.LookingAway,
                        Description = "Student looking away from screen",
                        Severity = ViolationSeverity.Low,
                        SnapshotUrl = snapshot.Id
                    });
                }

                if (snapshot.Analysis.PhoneDetected)
                {
                    await ReportViolationAsync(sessionId, new Violation
                    {
                        Type = ViolationType.PhoneDetected,
                        Description = "Mobile phone detected",
                        Severity = ViolationSeverity.Critical,
                        SnapshotUrl = snapshot.Id
                    });
                }

                if (snapshot.Analysis.BookDetected)
                {
                    await ReportViolationAsync(sessionId, new Violation
                    {
                        Type = ViolationType.BookDetected,
                        Description = "Books or notes detected",
                        Severity = ViolationSeverity.High,
                        SnapshotUrl = snapshot.Id
                    });
                }
            }

            // Store only last 50 snapshots to save space (or implement cleanup)
            session = await GetSessionAsync(sessionId);
            if (session != null)
            {
                session.Snapshots.Add(snapshot);
                if (session.Snapshots.Count > 50)
                {
                    session.Snapshots = session.Snapshots.Skip(session.Snapshots.Count - 50).ToList();
                }

                var update = Builders<ProctoringSession>.Update
                    .Set(s => s.Snapshots, session.Snapshots);

                var result = await _mongoDb.ProctoringSessions.UpdateOneAsync(
                    s => s.Id == sessionId,
                    update
                );

                return result.ModifiedCount > 0;
            }

            return false;
        }

        public async Task<bool> EndSessionAsync(string sessionId)
        {
            var update = Builders<ProctoringSession>.Update
                .Set(s => s.EndedAt, DateTime.UtcNow)
                .Set(s => s.Status, SessionStatus.Completed);

            var result = await _mongoDb.ProctoringSessions.UpdateOneAsync(
                s => s.Id == sessionId,
                update
            );

            return result.ModifiedCount > 0;
        }

        private double CalculateRiskScore(ProctoringSession session)
        {
            double score = 0;

            // Weighted scoring based on violation types
            score += session.TabSwitchCount * 2;
            score += session.MultipleFacesDetected * 10;
            score += session.NoFaceDetectedCount * 3;
            score += session.SuspiciousMovementCount * 1.5;
            score += session.AudioAnomaliesCount * 2;

            // Critical violations
            foreach (var violation in session.Violations)
            {
                if (violation.Severity == ViolationSeverity.Critical)
                    score += 20;
                else if (violation.Severity == ViolationSeverity.High)
                    score += 10;
                else if (violation.Severity == ViolationSeverity.Medium)
                    score += 5;
                else
                    score += 2;
            }

            return Math.Min(score, 100); // Cap at 100
        }

        private RiskLevel GetRiskLevel(double score)
        {
            if (score >= 70) return RiskLevel.Critical;
            if (score >= 40) return RiskLevel.High;
            if (score >= 20) return RiskLevel.Medium;
            return RiskLevel.Low;
        }
    }
}