using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace QuestionBankService.Models
{
    #region 1. Entities (Database Models)

    public class Question
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // Core Content
        [Required]
        public string QuestionText { get; set; } = string.Empty;
        public QuestionType Type { get; set; } = QuestionType.MCQ;
        public List<QuestionOption> Options { get; set; } = new(); 
        public string? CorrectAnswer { get; set; } 
        public List<string> CorrectOptions { get; set; } = new(); 

        // Scoring & Evaluation
        [Required]
        public double Marks { get; set; } = 1.0;
        public double? NegativeMarks { get; set; }
        public string? Explanation { get; set; }
        
        // Media & Formatting
        public string? ImageUrl { get; set; }
        public string? CodeSnippet { get; set; }

        // Metadata (Classification)
        public string DifficultyId { get; set; } = string.Empty; // Changed to support dynamic Difficulty CRUD
        public string? CategoryId { get; set; }
        public string? SubjectId { get; set; }
        public string? TopicId { get; set; }
        public List<string> Tags { get; set; } = new();

        // Lifecycle, Review & Versioning
        public QuestionStatus Status { get; set; } = QuestionStatus.Draft;
        public int Version { get; set; } = 1;
        public string? ParentQuestionId { get; set; } // Used if this is a duplicated question
        public List<ReviewLog> ReviewHistory { get; set; } = new();

        // Audit
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class QuestionVersion
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string OriginalQuestionId { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
        public Question SnapshotData { get; set; } = new(); // Complete copy of the question at that time
        
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class QuestionOption
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Text { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
    }

    public class ReviewLog
    {
        public string ReviewerId { get; set; } = string.Empty;
        public QuestionStatus Action { get; set; } // e.g., Approved, Rejected, PendingReview
        public string Comment { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // Dynamic Classifications (Supporting your Categories, Subjects, Topics, Tags, Difficulties endpoints)
    
    public class Category
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Subject
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string CategoryId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Topic
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SubjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Tag
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
    }

    public class Difficulty
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty; // e.g., "Easy", "Medium", "Hard", "Expert"
        public int Weight { get; set; } // Used for adaptive testing calculations
    }

    #endregion

    #region 2. Enums

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

    // Note: Replaced your old static DifficultyLevel enum with dynamic Difficulty entity above, 
    // but kept this enum in case you want to use it for backwards compatibility.
    public enum DifficultyLevel
    {
        Easy,
        Medium,
        Hard,
        Expert
    }

    public enum QuestionStatus
    {
        Draft,
        PendingReview,
        Approved,
        Rejected,
        Published,
        Archived
    }

    #endregion

    #region 3. Request DTOs

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
        
        public string DifficultyId { get; set; } = string.Empty;
        public string? CategoryId { get; set; }
        public string? SubjectId { get; set; }
        public string? TopicId { get; set; }
        public List<string> Tags { get; set; } = new();
        
        public string? Explanation { get; set; }
        public string? ImageUrl { get; set; }
        public string? CodeSnippet { get; set; }
    }

    public class OptionRequest
    {
        [Required]
        public string Text { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class ReviewCommentRequest
    {
        [Required]
        public string Comment { get; set; } = string.Empty;
    }

    public class ExportQuestionsRequest
    {
        public List<string>? CategoryIds { get; set; }
        public List<string>? SubjectIds { get; set; }
        public QuestionStatus? Status { get; set; }
        public string Format { get; set; } = "json"; // "json", "csv", "excel"
    }

    #endregion

    #region 4. Response DTOs

    public class QuestionDto
    {
        public string Id { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public List<QuestionOption> Options { get; set; } = new();
        public double Marks { get; set; }
        
        public string DifficultyId { get; set; } = string.Empty;
        public string? CategoryId { get; set; }
        public string? SubjectId { get; set; }
        public string? TopicId { get; set; }
        public List<string> Tags { get; set; } = new();
        
        public string? ImageUrl { get; set; }
        public string? CodeSnippet { get; set; }
        
        public QuestionStatus Status { get; set; }
        public int Version { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class QuestionWithAnswer : QuestionDto
    {
        public string? CorrectAnswer { get; set; }
        public List<string> CorrectOptions { get; set; } = new();
        public string? Explanation { get; set; }
        public List<ReviewLog> ReviewHistory { get; set; } = new();
    }

    #endregion
}