using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace NotificationService.Models
{
    public class Notification
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        public NotificationType Type { get; set; } = NotificationType.Info;

        public NotificationCategory Category { get; set; } = NotificationCategory.General;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        public Dictionary<string, object> Data { get; set; } = new();

        public string? ActionUrl { get; set; }

        public string? ImageUrl { get; set; }

        public List<NotificationChannel> Channels { get; set; } = new();

        public DeliveryStatus EmailStatus { get; set; } = DeliveryStatus.NotSent;

        public DeliveryStatus SMSStatus { get; set; } = DeliveryStatus.NotSent;

        public DeliveryStatus PushStatus { get; set; } = DeliveryStatus.NotSent;

        public DateTime? ExpiresAt { get; set; }
    }

    public class NotificationTemplate
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string BodyTemplate { get; set; } = string.Empty;

        public string? EmailTemplate { get; set; }

        public string? SMSTemplate { get; set; }

        public NotificationCategory Category { get; set; }

        public List<string> Variables { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
    }

    public class UserNotificationPreferences
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        public bool EmailEnabled { get; set; } = true;

        public bool SMSEnabled { get; set; } = false;

        public bool PushEnabled { get; set; } = true;

        public bool InAppEnabled { get; set; } = true;

        public Dictionary<NotificationCategory, bool> CategoryPreferences { get; set; } = new();

        public List<string> MutedCategories { get; set; } = new();

        public bool QuietHoursEnabled { get; set; } = false;

        public TimeSpan QuietHoursStart { get; set; } = TimeSpan.FromHours(22);

        public TimeSpan QuietHoursEnd { get; set; } = TimeSpan.FromHours(8);

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error,
        Reminder
    }

    public enum NotificationCategory
    {
        General,
        ExamScheduled,
        ExamStarting,
        ExamCompleted,
        ResultPublished,
        CourseEnrolled,
        NewLesson,
        LiveClassScheduled,
        LiveClassStarting,
        Assignment,
        Message,
        System,
        Violation,
        Achievement
    }

    public enum NotificationPriority
    {
        Low,
        Normal,
        High,
        Urgent
    }

    public enum NotificationChannel
    {
        InApp,
        Email,
        SMS,
        Push
    }

    public enum DeliveryStatus
    {
        NotSent,
        Pending,
        Sent,
        Failed,
        Bounced
    }

    public class SendNotificationRequest
    {
        public string? UserId { get; set; }
        public List<string>? UserIds { get; set; }
        public string? Role { get; set; } // Send to all users with this role
        
        [Required]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Message { get; set; } = string.Empty;
        
        public NotificationType Type { get; set; } = NotificationType.Info;
        public NotificationCategory Category { get; set; } = NotificationCategory.General;
        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
        public List<NotificationChannel> Channels { get; set; } = new() { NotificationChannel.InApp };
        public Dictionary<string, object>? Data { get; set; }
        public string? ActionUrl { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class BulkNotificationResult
    {
        public int TotalSent { get; set; }
        public int TotalFailed { get; set; }
        public List<string> SentNotificationIds { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}