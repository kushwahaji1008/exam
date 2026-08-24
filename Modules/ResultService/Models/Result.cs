using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ResultService.Models
{
    // ==========================================
    // 1. DATABASE ENTITIES
    // ==========================================
    public class ExamResult
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required] public string AttemptId { get; set; } = string.Empty;
        [Required] public string ExamId { get; set; } = string.Empty;
        [Required] public string UserId { get; set; } = string.Empty;

        public ResultStatus Status { get; set; } = ResultStatus.PendingCalculation;
        
        public double TotalScore { get; set; }
        public double MaxScore { get; set; }
        public double PassingScore { get; set; }
        public bool IsPassed { get; set; }
        public double Percentage => MaxScore > 0 ? (TotalScore / MaxScore) * 100 : 0;

        public List<QuestionGrade> Breakdown { get; set; } = new();

        public DateTime? CalculatedAt { get; set; }
        public DateTime? FinalizedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
    }

    public class QuestionGrade
    {
        public string QuestionId { get; set; } = string.Empty;
        public double Score { get; set; }
        public double MaxScore { get; set; }
        public bool IsCorrect { get; set; }
        public bool NeedsManualGrading { get; set; } = false;
        public string? EvaluatorId { get; set; }
        public string? OverrideReason { get; set; }
    }

    public class Certificate
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required] public string ResultId { get; set; } = string.Empty;
        [Required] public string UserId { get; set; } = string.Empty;
        [Required] public string ExamId { get; set; } = string.Empty;

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
        public string CertificateUrl { get; set; } = string.Empty;
        public string VerificationCode { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();
    }

    // ==========================================
    // 2. ENUMS
    // ==========================================
    public enum ResultStatus
    {
        PendingCalculation,
        AwaitingManualGrading,
        Calculated,
        Finalized,
        Published
    }

    // ==========================================
    // 3. REQUEST DTOs
    // ==========================================
    public class GradeQuestionRequest
    {
        [Required] public double Score { get; set; }
        public string? Comments { get; set; }
    }

    public class OverrideGradeRequest
    {
        [Required] public double NewScore { get; set; }
        [Required] public string Reason { get; set; } = string.Empty;
    }

    public class BulkPublishRequest
    {
        [Required] public List<string> ResultIds { get; set; } = new();
    }

    public class GenerateCertificateRequest
    {
        [Required] public string ResultId { get; set; } = string.Empty;
    }

    public class ExportFilterRequest
    {
        public string? ExamId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}