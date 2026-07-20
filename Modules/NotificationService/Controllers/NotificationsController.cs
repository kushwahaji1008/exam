using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using NotificationService.Models;
using NotificationService.Services;

namespace NotificationService.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly NotificationManagementService _notificationService;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            NotificationManagementService notificationService,
            ILogger<NotificationsController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest request)
        {
            var notification = await _notificationService.SendNotificationAsync(request);
            return Ok(new { message = "Notification sent", notification });
        }

        [HttpPost("bulk")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> SendBulkNotification([FromBody] SendNotificationRequest request)
        {
            var result = await _notificationService.SendBulkNotificationAsync(request);
            return Ok(new { message = "Bulk notifications sent", result });
        }

        [HttpGet("my-notifications")]
        [Authorize]
        public async Task<IActionResult> GetMyNotifications([FromQuery] bool unreadOnly = false, [FromQuery] int limit = 50)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var notifications = await _notificationService.GetUserNotificationsAsync(userId, unreadOnly, limit);
            return Ok(notifications);
        }

        [HttpGet("unread-count")]
        [Authorize]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var count = await _notificationService.GetUnreadCountAsync(userId);
            return Ok(new { count });
        }

        [HttpPost("{notificationId}/read")]
        [Authorize]
        public async Task<IActionResult> MarkAsRead(string notificationId)
        {
            var success = await _notificationService.MarkAsReadAsync(notificationId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to mark as read" });
            }

            return Ok(new { message = "Marked as read" });
        }

        [HttpPost("mark-all-read")]
        [Authorize]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _notificationService.MarkAllAsReadAsync(userId);
            
            return Ok(new { message = "All notifications marked as read", success });
        }

        [HttpDelete("{notificationId}")]
        [Authorize]
        public async Task<IActionResult> DeleteNotification(string notificationId)
        {
            var success = await _notificationService.DeleteNotificationAsync(notificationId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to delete notification" });
            }

            return Ok(new { message = "Notification deleted" });
        }

        [HttpDelete("delete-all")]
        [Authorize]
        public async Task<IActionResult> DeleteAllNotifications()
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _notificationService.DeleteAllNotificationsAsync(userId);
            
            return Ok(new { message = "All notifications deleted", success });
        }

        [HttpGet("preferences")]
        [Authorize]
        public async Task<IActionResult> GetPreferences()
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var preferences = await _notificationService.GetUserPreferencesAsync(userId);
            return Ok(preferences);
        }

        [HttpPut("preferences")]
        [Authorize]
        public async Task<IActionResult> UpdatePreferences([FromBody] UserNotificationPreferences preferences)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _notificationService.UpdateUserPreferencesAsync(userId, preferences);
            
            if (!success)
            {
                return BadRequest(new { message = "Failed to update preferences" });
            }

            return Ok(new { message = "Preferences updated" });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "NotificationService", timestamp = DateTime.UtcNow });
        }
    }
}