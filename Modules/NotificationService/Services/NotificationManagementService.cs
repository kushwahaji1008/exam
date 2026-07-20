using MongoDB.Driver;
using MongoDB.Bson;
using NotificationService.Models;

namespace NotificationService.Services
{
    public class NotificationManagementService
    {
        private readonly MongoDbService _mongoDb;
        private readonly EmailService _emailService;
        private readonly ILogger<NotificationManagementService> _logger;

        public NotificationManagementService(
            MongoDbService mongoDb,
            EmailService emailService,
            ILogger<NotificationManagementService> logger)
        {
            _mongoDb = mongoDb;
            _emailService = emailService;
            _logger = logger;
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
                Channels = request.Channels,
                Data = request.Data ?? new Dictionary<string, object>(),
                ActionUrl = request.ActionUrl,
                ImageUrl = request.ImageUrl,
                CreatedAt = DateTime.UtcNow
            };

            // Check user preferences
            if (!string.IsNullOrEmpty(request.UserId))
            {
                var preferences = await GetUserPreferencesAsync(request.UserId);
                if (!ShouldSendNotification(preferences, notification))
                {
                    _logger.LogInformation("Notification blocked by user preferences for {UserId}", request.UserId);
                    return notification;
                }
            }

            await _mongoDb.Notifications.InsertOneAsync(notification);

            // Send through channels
            _ = Task.Run(async () =>
            {
                try
                {
                    if (request.Channels.Contains(NotificationChannel.Email))
                    {
                        notification.EmailStatus = await _emailService.SendEmailAsync(
                            request.UserId ?? string.Empty,
                            request.Title,
                            request.Message
                        ) ? DeliveryStatus.Sent : DeliveryStatus.Failed;
                    }

                    if (request.Channels.Contains(NotificationChannel.SMS))
                    {
                        // SMS implementation would go here
                        notification.SMSStatus = DeliveryStatus.NotSent;
                    }

                    if (request.Channels.Contains(NotificationChannel.Push))
                    {
                        // Push notification implementation would go here
                        notification.PushStatus = DeliveryStatus.NotSent;
                    }

                    // Update delivery status
                    var update = Builders<Notification>.Update
                        .Set(n => n.EmailStatus, notification.EmailStatus)
                        .Set(n => n.SMSStatus, notification.SMSStatus)
                        .Set(n => n.PushStatus, notification.PushStatus);

                    await _mongoDb.Notifications.UpdateOneAsync(n => n.Id == notification.Id, update);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending notification {NotificationId}", notification.Id);
                }
            });

            return notification;
        }

        public async Task<BulkNotificationResult> SendBulkNotificationAsync(SendNotificationRequest request)
        {
            var result = new BulkNotificationResult();
            List<string> userIds = new();

            // Get user IDs based on criteria
            if (request.UserIds != null && request.UserIds.Any())
            {
                userIds = request.UserIds;
            }
            else if (!string.IsNullOrEmpty(request.Role))
            {
                // Get all users with specific role
                var usersCollection = _mongoDb.UsersDatabase.GetCollection<BsonDocument>("users");
                var roleFilter = Builders<BsonDocument>.Filter.Eq("Role", GetRoleValue(request.Role));
                var users = await usersCollection.Find(roleFilter).ToListAsync();
                userIds = users.Select(u => u["_id"].AsString).ToList();
            }
            else if (!string.IsNullOrEmpty(request.UserId))
            {
                userIds = new List<string> { request.UserId };
            }

            // Send to each user
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
            var filterBuilder = Builders<Notification>.Filter;
            var filters = new List<FilterDefinition<Notification>>
            {
                filterBuilder.Eq(n => n.UserId, userId)
            };

            if (unreadOnly)
            {
                filters.Add(filterBuilder.Eq(n => n.IsRead, false));
            }

            var filter = filterBuilder.And(filters);

            return await _mongoDb.Notifications
                .Find(filter)
                .SortByDescending(n => n.CreatedAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<bool> MarkAsReadAsync(string notificationId)
        {
            var update = Builders<Notification>.Update
                .Set(n => n.IsRead, true)
                .Set(n => n.ReadAt, DateTime.UtcNow);

            var result = await _mongoDb.Notifications.UpdateOneAsync(n => n.Id == notificationId, update);
            return result.ModifiedCount > 0;
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
            return result.ModifiedCount > 0;
        }

        public async Task<int> GetUnreadCountAsync(string userId)
        {
            var filter = Builders<Notification>.Filter.And(
                Builders<Notification>.Filter.Eq(n => n.UserId, userId),
                Builders<Notification>.Filter.Eq(n => n.IsRead, false)
            );

            return (int)await _mongoDb.Notifications.CountDocumentsAsync(filter);
        }

        public async Task<bool> DeleteNotificationAsync(string notificationId)
        {
            var result = await _mongoDb.Notifications.DeleteOneAsync(n => n.Id == notificationId);
            return result.DeletedCount > 0;
        }

        public async Task<bool> DeleteAllNotificationsAsync(string userId)
        {
            var result = await _mongoDb.Notifications.DeleteManyAsync(n => n.UserId == userId);
            return result.DeletedCount > 0;
        }

        public async Task<UserNotificationPreferences> GetUserPreferencesAsync(string userId)
        {
            var preferences = await _mongoDb.Preferences.Find(p => p.UserId == userId).FirstOrDefaultAsync();
            
            if (preferences == null)
            {
                preferences = new UserNotificationPreferences
                {
                    UserId = userId
                };
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

            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }

        private bool ShouldSendNotification(UserNotificationPreferences preferences, Notification notification)
        {
            // Check if notifications are globally disabled for this channel
            if (notification.Channels.Contains(NotificationChannel.Email) && !preferences.EmailEnabled)
                return false;

            if (notification.Channels.Contains(NotificationChannel.SMS) && !preferences.SMSEnabled)
                return false;

            if (notification.Channels.Contains(NotificationChannel.Push) && !preferences.PushEnabled)
                return false;

            if (notification.Channels.Contains(NotificationChannel.InApp) && !preferences.InAppEnabled)
                return false;

            // Check if category is muted
            if (preferences.MutedCategories.Contains(notification.Category.ToString()))
                return false;

            // Check quiet hours
            if (preferences.QuietHoursEnabled && notification.Priority != NotificationPriority.Urgent)
            {
                var now = DateTime.UtcNow.TimeOfDay;
                if (IsInQuietHours(now, preferences.QuietHoursStart, preferences.QuietHoursEnd))
                    return false;
            }

            return true;
        }

        private bool IsInQuietHours(TimeSpan current, TimeSpan start, TimeSpan end)
        {
            if (start < end)
            {
                return current >= start && current <= end;
            }
            else
            {
                // Quiet hours span midnight
                return current >= start || current <= end;
            }
        }

        private int GetRoleValue(string role)
        {
            return role switch
            {
                "Student" => 0,
                "Teacher" => 1,
                "Admin" => 2,
                "SuperAdmin" => 3,
                _ => 0
            };
        }

        // Predefined notification templates
        public async Task SendExamReminderAsync(string studentId, string examTitle, DateTime examTime)
        {
            await SendNotificationAsync(new SendNotificationRequest
            {
                UserId = studentId,
                Title = "Exam Reminder",
                Message = $"Your exam '{examTitle}' is scheduled for {examTime:MMM dd, yyyy hh:mm tt}",
                Type = NotificationType.Reminder,
                Category = NotificationCategory.ExamScheduled,
                Priority = NotificationPriority.High,
                Channels = new List<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Email }
            });
        }

        public async Task SendResultPublishedAsync(string studentId, string examTitle, double percentage, string result)
        {
            await SendNotificationAsync(new SendNotificationRequest
            {
                UserId = studentId,
                Title = "Exam Results Published",
                Message = $"Your results for '{examTitle}' are now available. Score: {percentage:F1}% - {result}",
                Type = result == "Pass" ? NotificationType.Success : NotificationType.Info,
                Category = NotificationCategory.ResultPublished,
                Priority = NotificationPriority.Normal,
                Channels = new List<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Email }
            });
        }

        public async Task SendLiveClassReminderAsync(string studentId, string className, DateTime startTime)
        {
            await SendNotificationAsync(new SendNotificationRequest
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
}
