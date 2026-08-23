using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ExamAttemptService.Models
{
    #region Entities (Database Models)

    public class ExamAttempt
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string ExamId { get; set; } = string.Empty;

        [Required]
        public string StudentId { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;

        // Lifecycle & Timing
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ActualEndTime { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        
        // Pause/Resume tracking
        public DateTime? LastPausedAt { get; set; }
        public int TotalPausedSeconds { get; set; } = 0;
        
        // Admin overrides
        public int ExtraTimeGrantedMinutes { get; set; } = 0;
        public string? TerminationReason { get; set; }
        public string? InvalidationReason { get; set; }

        public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;

        // Examination Data
        public List<Answer> Answers { get; set; } = new();
        public Dictionary<string, bool> QuestionFlags { get; set; } = new(); // For "Mark for Review"
        
        // Navigation tracking (which questions the student has visited)
        public HashSet<string> VisitedQuestions { get; set; } = new();

        // Evaluation
        public int TimeSpentMinutes { get; set; }
        public double? Score { get; set; }
        public double? Percentage { get; set; }
        public string? Result { get; set; } // Pass/Fail
        public bool AutoSubmitted { get; set; } = false;

        // Advanced Activity Logging
        public List<AttemptEvent> ActivityLog { get; set; } = new();
    }

    public class Answer
    {
        public string QuestionId { get; set; } = string.Empty;
        public string? SelectedOption { get; set; } // For MCQ single
        public List<string> SelectedOptions { get; set; } = new(); // For multiple correct
        public string? TextAnswer { get; set; } // For subjective
        public string? CodeAnswer { get; set; } // For code evaluation
        public DateTime? AnsweredAt { get; set; }
        public DateTime? LastModifiedAt { get; set; }
        
        // Evaluation metadata
        public bool IsCorrect { get; set; } = false;
        public double MarksAwarded { get; set; } = 0;
    }

    public class AttemptEvent
    {
        public EventType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? QuestionId { get; set; } // Optional: contextual to a specific question
    }

    #endregion

    #region Enums

    public enum AttemptStatus
    {
        NotStarted,
        InProgress,
        Paused,       // Added for pausing logic
        Submitted,
        Evaluated,
        Expired,      // Time ran out
        Terminated,   // Forced end (e.g., cheating detected)
        Invalidated   // Admin marked as void
    }

    public enum EventType
    {
        Started,
        Paused,
        Resumed,
        Submitted,
        ForceSubmitted,
        Terminated,
        Invalidated,
        Restored,
        TimeExtended,
        AnswerSaved,
        FlagToggled,
        Navigation,       // Moved Next/Previous
        WindowBlurred,    // Tab switch/Loss of focus (Proctoring)
        WindowFocused,    // Returned to tab
        Disconnected,     // WebSocket/Client ping lost
        Reconnected,
        Sync              // Local storage sync
    }

    #endregion

    #region Request DTOs

    public class StartExamRequest
    {
        [Required]
        public string ExamId { get; set; } = string.Empty;
    }

    public class SubmitAnswerRequest
    {
        public string? SelectedOption { get; set; }
        public List<string> SelectedOptions { get; set; } = new();
        public string? TextAnswer { get; set; }
        public string? CodeAnswer { get; set; }
    }

    public class SubmitExamRequest
    {
        [Required]
        public string AttemptId { get; set; } = string.Empty;
    }

    public class TimerSyncRequest
    {
        public int ClientRemainingSeconds { get; set; }
        public string? CurrentQuestionId { get; set; }
    }

    public class ExtendTimeRequest
    {
        [Required]
        public int ExtraMinutes { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class ActivityLogRequest
    {
        [Required]
        public string Activity { get; set; } = string.Empty;
        public EventType? EventType { get; set; }
        public string? QuestionId { get; set; }
    }

    #endregion

    #region Response DTOs

    public class AttemptDto
    {
        public string Id { get; set; } = string.Empty;
        public string ExamId { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        
        public DateTime StartedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public AttemptStatus Status { get; set; }
        
        public int TimeSpentMinutes { get; set; }
        public int ExtraTimeGrantedMinutes { get; set; }
        
        public double? Score { get; set; }
        public double? Percentage { get; set; }
        public string? Result { get; set; }
        
        public int TotalQuestions { get; set; }
        public int AnsweredQuestions { get; set; }
        public int FlaggedQuestions { get; set; }
    }

    // DTO returned for the `/navigation` endpoint
    public class NavigationStateDto
    {
        public string AttemptId { get; set; } = string.Empty;
        public int TotalTimeRemainingSeconds { get; set; }
        public List<QuestionNavigationInfo> Questions { get; set; } = new();
    }

    public class QuestionNavigationInfo
    {
        public string QuestionId { get; set; } = string.Empty;
        public bool IsAnswered { get; set; }
        public bool IsFlagged { get; set; }
        public bool IsVisited { get; set; }
    }

    #endregion
}