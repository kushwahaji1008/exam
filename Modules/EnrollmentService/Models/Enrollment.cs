using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EnrollmentService.Models
{
    public enum EnrollmentStatus
    {
        PendingPayment = 1,
        Active = 2,
        Expired = 3,
        Revoked = 4
    }

    [BsonIgnoreExtraElements]
    public class Enrollment
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string EnrollmentId { get; set; } = $"ENR-{new Random().Next(10000000, 99999999)}";
        public string UserId { get; set; } = string.Empty; // 9-digit ID
        public string CourseId { get; set; } = string.Empty;
        
        public decimal CoinsPaid { get; set; }
        public string WalletTransactionId { get; set; } = string.Empty; // To trace back to wallet
        
        public EnrollmentStatus Status { get; set; } = EnrollmentStatus.PendingPayment;

        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
        public DateTime? ValidUntil { get; set; } // Agar course ki expiry date ho (e.g. 1 year)
    }
}