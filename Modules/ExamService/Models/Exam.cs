using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ExamService.Models
{
    public class Exam
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string CourseId { get; set; } = string.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Required]
        public int DurationMinutes { get; set; }

        public DateTime? ScheduledStartTime { get; set; }

        public DateTime? ScheduledEndTime { get; set; }

        public ExamType Type { get; set; } = ExamType.MCQ;

        public ExamStatus Status { get; set; } = ExamStatus.Draft;

        [Required]
        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public List<string> QuestionIds { get; set; } = new();

        public ExamSettings Settings { get; set; } = new();

        public int TotalMarks { get; set; }

        public int PassingMarks { get; set; }

        public List<string> AllowedStudents { get; set; } = new(); // Empty means all students

        public string? InstructionsHtml { get; set; }
    }

    public class ExamSettings
    {
        public bool RandomizeQuestions { get; set; } = false;
        public bool AllowReview { get; set; } = false;
        public bool ShowResultsImmediately { get; set; } = false;
        public bool EnableNegativeMarking { get; set; } = false;
        public double NegativeMarkingPercentage { get; set; } = 0.25; // 25% deduction
        public bool RequireProctoring { get; set; } = false;
        public bool PreventTabSwitch { get; set; } = false;
        public bool EnableAutoSubmit { get; set; } = true;
        public int? GracePeriodMinutes { get; set; } = 5;
    }

    public enum ExamType
    {
        MCQ,
        Subjective,
        Mixed,
        CodeEvaluation
    }

    public enum ExamStatus
    {
        Draft,
        Scheduled,
        Active,
        Completed,
        Cancelled
    }

    public class CreateExamRequest
    {
        public string CourseId { get; set; } = string.Empty;
        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Required]
        public int DurationMinutes { get; set; }

        public DateTime? ScheduledStartTime { get; set; }

        public ExamType Type { get; set; } = ExamType.MCQ;

        public List<string> QuestionIds { get; set; } = new();

        public ExamSettings Settings { get; set; } = new();

        public int TotalMarks { get; set; }

        public int PassingMarks { get; set; }

        public List<string> AllowedStudents { get; set; } = new();

        public string? InstructionsHtml { get; set; }
    }

    public class ExamDto
    {
        public string CourseId { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? ScheduledEndTime { get; set; }
        public ExamType Type { get; set; }
        public ExamStatus Status { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int QuestionCount { get; set; }
        public int TotalMarks { get; set; }
        public int PassingMarks { get; set; }
        public ExamSettings Settings { get; set; } = new();
    }
}
