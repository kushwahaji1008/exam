using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace QuestionBankService.Models
{
    public class Question
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string QuestionText { get; set; } = string.Empty;

        public QuestionType Type { get; set; } = QuestionType.MCQ;

        public List<QuestionOption> Options { get; set; } = new(); // For MCQ, Multiple Choice

        public string? CorrectAnswer { get; set; } // For Subjective/Code

        public List<string> CorrectOptions { get; set; } = new(); // For MCQ (can be multiple correct)

        [Required]
        public double Marks { get; set; } = 1.0;

        public double? NegativeMarks { get; set; }

        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Medium;

        public string? Category { get; set; }

        public List<string> Tags { get; set; } = new();

        public string? Explanation { get; set; }

        public string? ImageUrl { get; set; }

        public string? CodeSnippet { get; set; }

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class QuestionOption
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Text { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
    }

    public enum QuestionType
    {
        MCQ,              // Multiple Choice - Single Answer
        MultipleCorrect,  // Multiple Choice - Multiple Answers
        TrueFalse,
        Subjective,       // Short Answer
        LongAnswer,       // Essay Type
        CodeEvaluation,   // Programming Questions
        FillInTheBlanks
    }

    public enum DifficultyLevel
    {
        Easy,
        Medium,
        Hard,
        Expert
    }

    public class CreateQuestionRequest
    {
        [Required]
        public string QuestionText { get; set; } = string.Empty;

        public QuestionType Type { get; set; } = QuestionType.MCQ;

        public List<QuestionOption> Options { get; set; } = new();

        public string? CorrectAnswer { get; set; }

        public List<string> CorrectOptions { get; set; } = new();

        [Required]
        public double Marks { get; set; } = 1.0;

        public double? NegativeMarks { get; set; }

        public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Medium;

        public string? Category { get; set; }

        public List<string> Tags { get; set; } = new();

        public string? Explanation { get; set; }

        public string? ImageUrl { get; set; }

        public string? CodeSnippet { get; set; }
    }

    public class QuestionDto
    {
        public string Id { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public List<QuestionOption> Options { get; set; } = new();
        public double Marks { get; set; }
        public DifficultyLevel Difficulty { get; set; }
        public string? Category { get; set; }
        public List<string> Tags { get; set; } = new();
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class QuestionWithAnswer : QuestionDto
    {
        public string? CorrectAnswer { get; set; }
        public List<string> CorrectOptions { get; set; } = new();
        public string? Explanation { get; set; }
    }
}
