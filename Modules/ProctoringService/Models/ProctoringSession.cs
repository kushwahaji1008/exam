using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ProctoringService.Models
{
    public class ProctoringSession
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string AttemptId { get; set; } = string.Empty;

        [Required]
        public string StudentId { get; set; } = string.Empty;

        [Required]
        public string ExamId { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? EndedAt { get; set; }

        public SessionStatus Status { get; set; } = SessionStatus.Active;

        // Violations tracking
        public List<Violation> Violations { get; set; } = new();

        public int TotalViolations { get; set; } = 0;

        public int TabSwitchCount { get; set; } = 0;

        public int MultipleFacesDetected { get; set; } = 0;

        public int NoFaceDetectedCount { get; set; } = 0;

        public int SuspiciousMovementCount { get; set; } = 0;

        public int AudioAnomaliesCount { get; set; } = 0;

        // Risk assessment
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

        public double RiskScore { get; set; } = 0;

        // Snapshots
        public List<Snapshot> Snapshots { get; set; } = new();

        public bool IsLiveMonitored { get; set; } = false;

        public string? MonitoredBy { get; set; }
    }

    public class Violation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public ViolationType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public ViolationSeverity Severity { get; set; }
        public string? SnapshotUrl { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class Snapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ImageBase64 { get; set; } = string.Empty;
        public SnapshotType Type { get; set; }
        public AIAnalysis? Analysis { get; set; }
    }

    public class AIAnalysis
    {
        public int FaceCount { get; set; }
        public bool FaceDetected { get; set; }
        public bool MultipleFaces { get; set; }
        public bool LookingAway { get; set; }
        public bool PhoneDetected { get; set; }
        public bool BookDetected { get; set; }
        public double ConfidenceScore { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    public enum SessionStatus
    {
        Active,
        Completed,
        Flagged,
        Suspended
    }

    public enum ViolationType
    {
        TabSwitch,
        WindowSwitch,
        MultipleFaces,
        NoFaceDetected,
        LookingAway,
        PhoneDetected,
        BookDetected,
        SuspiciousMovement,
        AudioAnomaly,
        FullscreenExit,
        NetworkDisconnect,
        UnauthorizedDevice,
        CopyPasteAttempt,
        RightClickAttempt,
        ScreenRecordingDetected,
        VMDetected
    }

    public enum ViolationSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum SnapshotType
    {
        Periodic,
        ViolationTriggered,
        Manual,
        StartOfExam,
        EndOfExam
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class StartProctoringRequest
    {
        [Required]
        public string AttemptId { get; set; } = string.Empty;

        [Required]
        public string ExamId { get; set; } = string.Empty;
    }

    public class ReportViolationRequest
    {
        [Required]
        public string SessionId { get; set; } = string.Empty;

        [Required]
        public ViolationType Type { get; set; }

        public string Description { get; set; } = string.Empty;

        public ViolationSeverity Severity { get; set; } = ViolationSeverity.Medium;

        public string? SnapshotBase64 { get; set; }
    }

    public class SubmitSnapshotRequest
    {
        [Required]
        public string SessionId { get; set; } = string.Empty;

        [Required]
        public string ImageBase64 { get; set; } = string.Empty;

        public SnapshotType Type { get; set; } = SnapshotType.Periodic;
    }
}