using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace AnalyticsService.Models
{
    // ==========================================
    // 1. DATABASE ENTITIES (For Offline Reports)
    // ==========================================
    public class AnalyticsReport
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required] public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // e.g., "ExamPerformance", "Revenue"

        public ReportStatus Status { get; set; } = ReportStatus.Pending;

        public string RequestedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? GeneratedAt { get; set; }
        
        public string? DownloadUrl { get; set; }
        public string? ErrorMessage { get; set; }
        
        public Dictionary<string, string> Filters { get; set; } = new();
    }

    public enum ReportStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }

    // ==========================================
    // 2. REQUEST DTOs
    // ==========================================
    public class CreateReportRequest
    {
        [Required] public string Name { get; set; } = string.Empty;
        [Required] public string Type { get; set; } = string.Empty;
        public Dictionary<string, string> Filters { get; set; } = new();
    }
}