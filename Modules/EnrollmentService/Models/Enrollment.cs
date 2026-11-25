using System.Security.Cryptography;
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

        public string EnrollmentId { get; set; } = $"ENR-{RandomNumberGenerator.GetInt32(10_000_000, 100_000_000)}";
        public string UserId { get; set; } = string.Empty; // 9-digit ID
        public string CourseId { get; set; } = string.Empty;

        public decimal CoinsPaid { get; set; }
        public string WalletTransactionId { get; set; } = string.Empty;

        public EnrollmentStatus Status { get; set; } = EnrollmentStatus.PendingPayment;

        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
        public DateTime? ValidUntil { get; set; }
    }
}
