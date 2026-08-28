using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace CourseService.Models.V1
{
    // ==========================================
    // 1. DATABASE ENTITIES
    // ==========================================

    public class Course
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string CourseId { get; set; } = string.Empty; // Backend generated (e.g. CRS-123456)

        [Required]
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;

        public CourseStatus Status { get; set; } = CourseStatus.Draft;
        public CourseLevel Level { get; set; } = CourseLevel.Beginner;

        // 👇 Changed to CoursePrice and added Range validation
        [Range(0, 1000000, ErrorMessage = "Price cannot be negative.")]
        public decimal CoursePrice { get; set; } = 0.0m; 
        
        [Range(0, 1000000, ErrorMessage = "Discount Price cannot be negative.")]
        public decimal DiscountCoursePrice { get; set; } = 0.0m;
        
        public int EnrollmentCount { get; set; } = 0;

        [Required]
        public string CreatedBy { get; set; } = string.Empty; // 9-Digit User ID
        public List<string> InstructorIds { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public List<CourseSection> Sections { get; set; } = new();
    }

    public class CourseSection
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string Title { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        
        public List<CurriculumItem> Items { get; set; } = new();
    }

    public class CurriculumItem
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string Title { get; set; } = string.Empty;
        public CurriculumType Type { get; set; } // Video, Quiz, Document
        public string ContentUrl { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public bool IsFreePreview { get; set; } = false;
        public int DurationSeconds { get; set; } = 0;
    }

    // ==========================================
    // 2. ENUMS
    // ==========================================

    public enum CourseStatus
    {
        Draft,
        Published,
        Archived
    }

    public enum CourseLevel
    {
        Beginner,
        Intermediate,
        Advanced,
        AllLevels
    }

    public enum CurriculumType
    {
        Video,
        Document,
        Quiz,
        Assignment
    }

    // ==========================================
    // 3. REQUEST DTOs
    // ==========================================

    public class CreateCourseRequest
    {
        
        [Required] 
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CourseLevel Level { get; set; } = CourseLevel.Beginner;
        
        [Range(0, 1000000, ErrorMessage = "Price cannot be negative.")]
        public decimal CoursePrice { get; set; } = 0;
    }

    public class CreateSectionRequest
    {
        [Required] public string Title { get; set; } = string.Empty;
    }

    public class CreateCurriculumItemRequest
    {
        [Required] public string Title { get; set; } = string.Empty;
        [Required] public CurriculumType Type { get; set; }
        public string ContentUrl { get; set; } = string.Empty;
        public bool IsFreePreview { get; set; } = false;
        public int DurationSeconds { get; set; } = 0;
    }

    public class ReorderRequest
    {
        [Required] public List<string> OrderedIds { get; set; } = new();
    }

    public class AssignInstructorRequest
    {
        [Required] public string InstructorId { get; set; } = string.Empty;
    }
}