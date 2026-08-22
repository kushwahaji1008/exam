using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ExamService.Models
{
    // ==========================================
    // 1. CORE DATABASE ENTITIES
    // ==========================================

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

        // Question & Section Management
        public List<string> QuestionIds { get; set; } = new();
        public List<ExamSection> Sections { get; set; } = new();

        // Exam Configuration
        public ExamSettings Settings { get; set; } = new();
        public ExamGrading Grading { get; set; } = new();
        public string? InstructionsHtml { get; set; }

        public int TotalMarks { get; set; }
        public int PassingMarks { get; set; }

        // Access Management
        public List<string> AllowedStudents { get; set; } = new(); // Empty = Public/All
        public List<string> BlockedStudents { get; set; } = new(); // Explicitly banned

        // Version Control
        public string Version { get; set; } = "1.0";
        public string? ParentExamId { get; set; } 
        public bool IsLatestVersion { get; set; } = true;
    }

    public class ExamSection
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int OrderIndex { get; set; } = 0;
        public List<string> QuestionIds { get; set; } = new();
    }

    public class ExamSettings
    {
        public bool RandomizeQuestions { get; set; } = false;
        public bool RandomizeSections { get; set; } = false;
        public bool AllowReview { get; set; } = false;
        public bool ShowResultsImmediately { get; set; } = false;
        public bool RequireProctoring { get; set; } = false;
        public bool PreventTabSwitch { get; set; } = false;
        public bool EnableAutoSubmit { get; set; } = true;
        public int? GracePeriodMinutes { get; set; } = 5;
    }

    public class ExamGrading
    {
        public bool EnableNegativeMarking { get; set; } = false;
        public double NegativeMarkingPercentage { get; set; } = 0.25; 
        public bool SectionalPassMarksEnabled { get; set; } = false; 
    }

    // ==========================================
    // 2. ENUMS
    // ==========================================

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
        Published,
        Scheduled,
        Active,
        Completed,
        Cancelled,
        Archived
    }

    // ==========================================
    // 3. REQUEST DTOs (Incoming Data)
    // ==========================================

    public class CreateExamRequest
    {
        public string CourseId { get; set; } = string.Empty;
        
        [Required]
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        [Required]
        public int DurationMinutes { get; set; }
        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? ScheduledEndTime { get; set; }
        
        public ExamType Type { get; set; } = ExamType.MCQ;

        public List<string> QuestionIds { get; set; } = new();
        public ExamSettings Settings { get; set; } = new();
        public ExamGrading Grading { get; set; } = new();

        public int TotalMarks { get; set; }
        public int PassingMarks { get; set; }

        public List<string> AllowedStudents { get; set; } = new();
        public string? InstructionsHtml { get; set; }
    }

    public class UpdateInstructionsRequest
    {
        [Required] public string InstructionsHtml { get; set; } = string.Empty;
    }

    public class ReorderRequest
    {
        [Required] public List<string> OrderedIds { get; set; } = new();
    }

    public class CreateSectionRequest
    {
        [Required] public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class UpdateSectionRequest : CreateSectionRequest 
    {
        public List<string> QuestionIds { get; set; } = new();
    }

    public class BulkCandidatesRequest
    {
        [Required] public List<string> UserIds { get; set; } = new();
    }
    public class ScheduleRequest
    {
        [Required] 
        public DateTime StartTime { get; set; }
        
        public DateTime? EndTime { get; set; }
    }

    // ==========================================
    // 4. RESPONSE DTOs (Outgoing Data)
    // ==========================================

    public class ExamDto
    {
        public string Id { get; set; } = string.Empty;
        public string CourseId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? ScheduledEndTime { get; set; }
        public ExamType Type { get; set; }
        public ExamStatus Status { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Version { get; set; } = "1.0";
        public int QuestionCount { get; set; }
        public int SectionCount { get; set; }
        public int TotalMarks { get; set; }
        public int PassingMarks { get; set; }
        public ExamSettings Settings { get; set; } = new();
        public ExamGrading Grading { get; set; } = new();
    }

    public class SectionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public int QuestionCount { get; set; }
    }

    public class ExamVersionDto
    {
        public string VersionId { get; set; } = string.Empty; // E.g., v1.1
        public string ExamId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public bool IsActiveVersion { get; set; }
    }

    // ==========================================
    // 5. ANALYTICS & STATISTICS DTOs
    // ==========================================

    public class ExamStatisticsDto
    {
        public string ExamId { get; set; } = string.Empty;
        public int TotalAttempts { get; set; }
        public int TotalCompleted { get; set; }
        public double AverageScore { get; set; }
        public double HighestScore { get; set; }
        public double LowestScore { get; set; }
        public double PassRatePercentage { get; set; }
    }

    public class PerformanceMetricsDto
    {
        public Dictionary<string, double> AverageScoreBySection { get; set; } = new();
        public double AverageCompletionTimeMinutes { get; set; }
        public int TopPercentileThreshold { get; set; }
    }

    public class CompletionStatsDto
    {
        public double CompletionRatePercentage { get; set; }
        public double DropoutRatePercentage { get; set; }
        public string AverageTimeToComplete { get; set; } = string.Empty; // e.g., "45m 30s"
    }

    public class QuestionAnalysisDto
    {
        public List<QuestionMetricDto> ToughestQuestions { get; set; } = new();
        public List<QuestionMetricDto> EasiestQuestions { get; set; } = new();
    }

    public class QuestionMetricDto
    {
        public string QuestionId { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public int CorrectCount { get; set; }
        public double SuccessRatePercentage { get; set; } 
    }
}