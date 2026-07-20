using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ExamAttemptService.Models
{
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

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? SubmittedAt { get; set; }

        public DateTime? ActualEndTime { get; set; }

        public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;

        public List<Answer> Answers { get; set; } = new();

        public Dictionary<string, bool> QuestionFlags { get; set; } = new(); // For review later

        public int TimeSpentMinutes { get; set; }

        public double? Score { get; set; }

        public double? Percentage { get; set; }

        public string? Result { get; set; } // Pass/Fail

        public bool AutoSubmitted { get; set; } = false;

        public List<string> ActivityLog { get; set; } = new(); // Tab switches, etc.
    }

    public class Answer
    {
        public string QuestionId { get; set; } = string.Empty;
        public string? SelectedOption { get; set; } // For MCQ single
        public List<string> SelectedOptions { get; set; } = new(); // For multiple correct
        public string? TextAnswer { get; set; } // For subjective
        public string? CodeAnswer { get; set; } // For code evaluation
        public DateTime? AnsweredAt { get; set; }
        public bool IsCorrect { get; set; } = false;
        public double MarksAwarded { get; set; } = 0;
    }

    public enum AttemptStatus
    {
        NotStarted,
        InProgress,
        Submitted,
        Evaluated,
        Expired
    }

    public class StartExamRequest
    {
        [Required]
        public string ExamId { get; set; } = string.Empty;
    }

    public class SubmitAnswerRequest
    {
        [Required]
        public string QuestionId { get; set; } = string.Empty;
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
        public double? Score { get; set; }
        public double? Percentage { get; set; }
        public string? Result { get; set; }
        public int TotalQuestions { get; set; }
        public int AnsweredQuestions { get; set; }
    }
}