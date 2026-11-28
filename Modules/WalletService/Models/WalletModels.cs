using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WalletService.Models
{
    [BsonIgnoreExtraElements]
    public class Wallet
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string UserId { get; set; } = string.Empty; // 9-digit Student ID
        public decimal Balance { get; set; } = 0;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    [BsonIgnoreExtraElements]
    public class WalletTransaction
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string UserId { get; set; } = string.Empty;
        
        // IDEMPOTENCY KEY: Network retry par duplicate transaction block karne ke liye
        public string IdempotencyKey { get; set; } = string.Empty; 

        public decimal Amount { get; set; } // Hamesha positive hoga, Type batayega Debit/Credit
        public string Type { get; set; } = string.Empty; // "CREDIT" or "DEBIT"
        public string Description { get; set; } = string.Empty; 
        
        public string ReferenceId { get; set; } = string.Empty; // CourseId, ExamId, SeriesId, or ReferralCode
        public string SourceService { get; set; } = string.Empty; // "EnrollmentService", "ExamService", "ReferralProgram"

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    [BsonIgnoreExtraElements]
    public class ReferralRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ReferrerUserId { get; set; } = string.Empty;
        public string ReferrerName { get; set; } = string.Empty;
        public string ReferredUserId { get; set; } = string.Empty;
        public string ReferredUserName { get; set; } = string.Empty;
        public string ReferredUserEmail { get; set; } = string.Empty;
        public string ReferralCode { get; set; } = string.Empty;

        public decimal ReferrerCoins { get; set; } = 100m;
        public decimal ReferredCoins { get; set; } = 100m;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}