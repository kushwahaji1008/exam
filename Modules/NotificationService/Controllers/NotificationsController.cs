using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Models;
using NotificationService.Services;

namespace NotificationService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/notifications")]
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

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private bool IsStaff() => User.FindFirst(ClaimTypes.Role)?.Value is "Teacher" or "Admin" or "SuperAdmin";

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest request)
        {
            var callerId = GetUserId();
            if (!IsStaff() && !string.Equals(request.UserId, callerId, StringComparison.Ordinal))
                return Forbid();

            var notification = await _notificationService.SendNotificationAsync(request);
            return Ok(new { message = "Notification sent", notification });
        }

        [HttpPost("bulk")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> SendBulkNotification([FromBody] SendNotificationRequest request)
        {
            var result = await _notificationService.SendBulkNotificationAsync(request);
            return Ok(new { message = "Bulk notifications sent", result });
        }

        [HttpGet("my-notifications")]
        [Authorize]
        public async Task<IActionResult> GetMyNotifications([FromQuery] bool unreadOnly = false, [FromQuery] int limit = 50)
        {
            var userId = GetUserId() ?? string.Empty;
            var notifications = await _notificationService.GetUserNotificationsAsync(userId, unreadOnly, limit);
            return Ok(notifications);
        }

        [HttpGet("unread-count")]
        [Authorize]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetUserId() ?? string.Empty;
            var count = await _notificationService.GetUnreadCountAsync(userId);
            return Ok(new { count });
        }

        [HttpPost("{notificationId}/read")]
        [Authorize]
        public async Task<IActionResult> MarkAsRead(string notificationId)
        {
            var callerId = GetUserId() ?? string.Empty;
            var existing = await _notificationService.GetNotificationByIdAsync(notificationId);
            if (existing == null) return NotFound(new { message = "Notification not found" });
            if (!IsStaff() && existing.UserId != callerId) return Forbid();

            var success = await _notificationService.MarkAsReadAsync(notificationId);
            return success ? Ok(new { message = "Marked as read" }) : BadRequest(new { message = "Failed to mark as read" });
        }

        [HttpPost("mark-all-read")]
        [Authorize]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = GetUserId() ?? string.Empty;
            var success = await _notificationService.MarkAllAsReadAsync(userId);
            return Ok(new { message = "All notifications marked as read", success });
        }

        [HttpDelete("{notificationId}")]
        [Authorize]
        public async Task<IActionResult> DeleteNotification(string notificationId)
        {
            var callerId = GetUserId() ?? string.Empty;
            var existing = await _notificationService.GetNotificationByIdAsync(notificationId);
            if (existing == null) return NotFound(new { message = "Notification not found" });
            if (!IsStaff() && existing.UserId != callerId) return Forbid();

            var success = await _notificationService.DeleteNotificationAsync(notificationId);
            return success ? Ok(new { message = "Notification deleted" }) : BadRequest(new { message = "Failed to delete notification" });
        }

        [HttpDelete("delete-all")]
        [Authorize]
        public async Task<IActionResult> DeleteAllNotifications()
        {
            var userId = GetUserId() ?? string.Empty;
            var success = await _notificationService.DeleteAllNotificationsAsync(userId);
            return Ok(new { message = "All notifications deleted", success });
        }

        [HttpGet("preferences")]
        [Authorize]
        public async Task<IActionResult> GetPreferences()
        {
            var userId = GetUserId() ?? string.Empty;
            var preferences = await _notificationService.GetUserPreferencesAsync(userId);
            return Ok(preferences);
        }

        [HttpPut("preferences")]
        [Authorize]
        public async Task<IActionResult> UpdatePreferences([FromBody] UserNotificationPreferences preferences)
        {
            var userId = GetUserId() ?? string.Empty;
            var success = await _notificationService.UpdateUserPreferencesAsync(userId, preferences);
            if (!success) return BadRequest(new { message = "Failed to update preferences" });

            return Ok(new { message = "Preferences updated" });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "NotificationService", timestamp = DateTime.UtcNow });
        }
    }
}
