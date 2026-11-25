using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Services
{
    public class NotificationManagementService
    {
        private readonly MongoDbService _mongoDb;
        private readonly EmailService _emailService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<NotificationManagementService> _logger;

        public NotificationManagementService(
            MongoDbService mongoDb,
            EmailService emailService,
            ILogger<NotificationManagementService> logger,
            IHubContext<NotificationHub>? hubContext = null)
        {
            _mongoDb = mongoDb;
            _emailService = emailService;
            _logger = logger;
            _hubContext = hubContext!;
        }

        public async Task<Notification> SendNotificationAsync(SendNotificationRequest request)
        {
            var notification = new Notification
            {
                UserId = request.UserId ?? string.Empty,
                Title = request.Title,
                Message = request.Message,
                Type = request.Type,
                Category = request.Category,
                Priority = request.Priority,
                Channels = request.Channels ?? new List<NotificationChannel> { NotificationChannel.InApp },
                Data = request.Data ?? new Dictionary<string, object>(),
                ActionUrl = request.ActionUrl,
                ImageUrl = request.ImageUrl,
                CreatedAt = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(request.UserId))
            {
                var preferences = await GetUserPreferencesAsync(request.UserId);
                if (!ShouldSendNotification(preferences, notification))
                {
                    _logger.LogInformation("Notification blocked by user preferences for {UserId}", request.UserId);
                    return notification;
                }
            }

            if (notification.Channels.Contains(NotificationChannel.Email) && !string.IsNullOrWhiteSpace(request.UserId))
            {
                var email = await ResolveUserEmailAsync(request.UserId);
                if (!string.IsNullOrEmpty(email))
                {
                    notification.EmailStatus = await _emailService.SendEmailAsync(email, request.Title, request.Message)
                        ? DeliveryStatus.Sent
                        : DeliveryStatus.Failed;
                }
                else
                {
                    notification.EmailStatus = DeliveryStatus.Failed;
                }
            }

            await _mongoDb.Notifications.InsertOneAsync(notification);

            // Real-time SignalR push to connected client
            if (_hubContext != null && !string.IsNullOrEmpty(notification.UserId) && notification.Channels.Contains(NotificationChannel.InApp))
            {
                try
                {
                    await _hubContext.Clients.Group($"user_{notification.UserId}").SendAsync("ReceiveNotification", notification);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR push failed for user {UserId}", notification.UserId);
                }
            }

            return notification;
        }

        private async Task<string?> ResolveUserEmailAsync(string userIdOrEmail)
        {
            if (userIdOrEmail.Contains('@')) return userIdOrEmail;

            try
            {
                var usersCollection = _mongoDb.UsersDatabase.GetCollection<BsonDocument>("users");
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("UserId", userIdOrEmail),
                        Builders<BsonDocument>.Filter.Eq("_id", userIdOrEmail)),
                    Builders<BsonDocument>.Filter.Ne("IsDeleted", true));
                var doc = await usersCollection.Find(filter).FirstOrDefaultAsync();
                if (doc != null && doc.TryGetValue("Email", out var emailVal) && emailVal.IsString)
                    return emailVal.AsString;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed resolving email for UserId {UserId}", userIdOrEmail);
            }
            return null;
        }

        public async Task<BulkNotificationResult> SendBulkNotificationAsync(SendNotificationRequest request)
        {
            var result = new BulkNotificationResult();
            var userIds = new List<string>();

            if (request.UserIds != null && request.UserIds.Any())
            {
                userIds = request.UserIds.Distinct().ToList();
            }
            else if (!string.IsNullOrEmpty(request.Role))
            {
                var usersCollection = _mongoDb.UsersDatabase.GetCollection<BsonDocument>("users");
                var roleFilter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("Role", GetRoleValue(request.Role)),
                    Builders<BsonDocument>.Filter.Ne("IsDeleted", true));
                var users = await usersCollection.Find(roleFilter).ToListAsync();
                userIds = users
                    .Select(u => u.TryGetValue("UserId", out var uid) && uid.IsString ? uid.AsString : (u.TryGetValue("_id", out var oid) && oid.IsString ? oid.AsString : string.Empty))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();
            }
            else if (!string.IsNullOrEmpty(request.UserId))
            {
                userIds = new List<string> { request.UserId };
            }

            foreach (var userId in userIds)
            {
                try
                {
                    var userRequest = new SendNotificationRequest
                    {
                        UserId = userId,
                        Title = request.Title,
                        Message = request.Message,
                        Type = request.Type,
                        Category = request.Category,
                        Priority = request.Priority,
                        Channels = request.Channels,
                        Data = request.Data,
                        ActionUrl = request.ActionUrl,
                        ImageUrl = request.ImageUrl
                    };

                    var notification = await SendNotificationAsync(userRequest);
                    result.SentNotificationIds.Add(notification.Id);
                    result.TotalSent++;
                }
                catch (Exception ex)
                {
                    result.TotalFailed++;
                    result.Errors.Add($"User {userId}: {ex.Message}");
                    _logger.LogError(ex, "Failed to send notification to user {UserId}", userId);
                }
            }

            return result;
        }

        public async Task<List<Notification>> GetUserNotificationsAsync(string userId, bool unreadOnly = false, int limit = 50)
        {
            var safeLimit = Math.Clamp(limit, 1, 200);
            var fb = Builders<Notification>.Filter;
            var filters = new List<FilterDefinition<Notification>> { fb.Eq(n => n.UserId, userId) };
            if (unreadOnly) filters.Add(fb.Eq(n => n.IsRead, false));

            return await _mongoDb.Notifications
                .Find(fb.And(filters))
                .SortByDescending(n => n.CreatedAt)
                .Limit(safeLimit)
                .ToListAsync();
        }

        public async Task<Notification?> GetNotificationByIdAsync(string notificationId) =>
            await _mongoDb.Notifications.Find(n => n.Id == notificationId).FirstOrDefaultAsync();

        public async Task<bool> MarkAsReadAsync(string notificationId, string? userId = null)
        {
            var fb = Builders<Notification>.Filter;
            var filter = string.IsNullOrEmpty(userId)
                ? fb.Eq(n => n.Id, notificationId)
                : fb.And(fb.Eq(n => n.Id, notificationId), fb.Eq(n => n.UserId, userId));

            var update = Builders<Notification>.Update
                .Set(n => n.IsRead, true)
                .Set(n => n.ReadAt, DateTime.UtcNow);

            var result = await _mongoDb.Notifications.UpdateOneAsync(filter, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> MarkAllAsReadAsync(string userId)
        {
            var update = Builders<Notification>.Update
                .Set(n => n.IsRead, true)
                .Set(n => n.ReadAt, DateTime.UtcNow);

            var filter = Builders<Notification>.Filter.And(
                Builders<Notification>.Filter.Eq(n => n.UserId, userId),
                Builders<Notification>.Filter.Eq(n => n.IsRead, false)
            );

            var result = await _mongoDb.Notifications.UpdateManyAsync(filter, update);
            return result.MatchedCount > 0 || result.ModifiedCount >= 0;
        }

        public async Task<int> GetUnreadCountAsync(string userId)
        {
            var filter = Builders<Notification>.Filter.And(
                Builders<Notification>.Filter.Eq(n => n.UserId, userId),
                Builders<Notification>.Filter.Eq(n => n.IsRead, false)
            );

            return (int)await _mongoDb.Notifications.CountDocumentsAsync(filter);
        }

        public async Task<bool> DeleteNotificationAsync(string notificationId, string? userId = null)
        {
            var fb = Builders<Notification>.Filter;
            var filter = string.IsNullOrEmpty(userId)
                ? fb.Eq(n => n.Id, notificationId)
                : fb.And(fb.Eq(n => n.Id, notificationId), fb.Eq(n => n.UserId, userId));

            var result = await _mongoDb.Notifications.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        public async Task<bool> DeleteAllNotificationsAsync(string userId)
        {
            var result = await _mongoDb.Notifications.DeleteManyAsync(n => n.UserId == userId);
            return result.DeletedCount >= 0;
        }

        public async Task<UserNotificationPreferences> GetUserPreferencesAsync(string userId)
        {
            var preferences = await _mongoDb.Preferences.Find(p => p.UserId == userId).FirstOrDefaultAsync();
            if (preferences == null)
            {
                preferences = new UserNotificationPreferences { UserId = userId };
                await _mongoDb.Preferences.InsertOneAsync(preferences);
            }
            return preferences;
        }

        public async Task<bool> UpdateUserPreferencesAsync(string userId, UserNotificationPreferences preferences)
        {
            preferences.UserId = userId;
            preferences.UpdatedAt = DateTime.UtcNow;

            var result = await _mongoDb.Preferences.ReplaceOneAsync(
                p => p.UserId == userId,
                preferences,
                new ReplaceOptions { IsUpsert = true }
            );

            return result.MatchedCount > 0 || result.UpsertedId != null;
        }

        private static bool ShouldSendNotification(UserNotificationPreferences preferences, Notification notification)
        {
            if (notification.Channels.Contains(NotificationChannel.InApp) && !preferences.InAppEnabled)
                return false;

            if (preferences.MutedCategories.Contains(notification.Category.ToString()))
                return false;

            if (preferences.QuietHoursEnabled && notification.Priority != NotificationPriority.Urgent)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(preferences.TimeZoneId ?? "UTC");
                    var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).TimeOfDay;
                    if (IsInQuietHours(now, preferences.QuietHoursStart, preferences.QuietHoursEnd))
                        return false;
                }
                catch
                {
                    var now = DateTime.UtcNow.TimeOfDay;
                    if (IsInQuietHours(now, preferences.QuietHoursStart, preferences.QuietHoursEnd))
                        return false;
                }
            }

            return true;
        }

        private static bool IsInQuietHours(TimeSpan current, TimeSpan start, TimeSpan end) =>
            start < end ? (current >= start && current <= end) : (current >= start || current <= end);

        private static int GetRoleValue(string role) => role switch
        {
            "Student" => 0,
            "Teacher" => 1,
            "Admin" => 2,
            "SuperAdmin" => 3,
            _ => 0
        };

        public Task SendExamReminderAsync(string studentId, string examTitle, DateTime examTime) =>
            SendNotificationAsync(new SendNotificationRequest
            {
                UserId = studentId,
                Title = "Exam Reminder",
                Message = $"Your exam '{examTitle}' is scheduled for {examTime:MMM dd, yyyy hh:mm tt}",
                Type = NotificationType.Reminder,
                Category = NotificationCategory.ExamScheduled,
                Priority = NotificationPriority.High,
                Channels = new List<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Email }
            });

        public Task SendResultPublishedAsync(string studentId, string examTitle, double percentage, string result) =>
            SendNotificationAsync(new SendNotificationRequest
            {
                UserId = studentId,
                Title = "Exam Results Published",
                Message = $"Your results for '{examTitle}' are now available. Score: {percentage:F1}% - {result}",
                Type = result == "Pass" ? NotificationType.Success : NotificationType.Info,
                Category = NotificationCategory.ResultPublished,
                Priority = NotificationPriority.Normal,
                Channels = new List<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Email }
            });

        public Task SendLiveClassReminderAsync(string studentId, string className, DateTime startTime) =>
            SendNotificationAsync(new SendNotificationRequest
            {
                UserId = studentId,
                Title = "Live Class Starting Soon",
                Message = $"'{className}' will start in 15 minutes at {startTime:hh:mm tt}",
                Type = NotificationType.Reminder,
                Category = NotificationCategory.LiveClassStarting,
                Priority = NotificationPriority.High,
                Channels = new List<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Push }
            });
    }
}
