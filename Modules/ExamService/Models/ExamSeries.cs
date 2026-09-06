using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ExamService.Models
{
    public class ExamSeries
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string SeriesId { get; set; } = string.Empty; // Backend generated e.g. EXS-123456

        [Required]
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;

        // Security: Price hacker block
        [Range(0, 1000)]
        public decimal Price { get; set; } = 0.0m;
        
        [Range(0, 1000)]
        public decimal DiscountPrice { get; set; } = 0.0m;

        public bool IsPublished { get; set; } = false;

        public List<string> ExamIds { get; set; } = new(); // Linked Exams (Tests)

        [Required]
        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    // --- DTOs ---
    public class CreateExamSeriesRequest
    {
        [Required] public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        [Range(0, 1000)] public decimal Price { get; set; }
    }

    public class AddExamToSeriesRequest
    {
        [Required] public string ExamId { get; set; } = string.Empty;
    }
}