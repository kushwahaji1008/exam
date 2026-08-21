using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AuthService.Models;
using AuthService.Services;

namespace AuthService.V2.Controllers
{
    [ApiController]
    [Route("api/v2/users")]
    [Authorize(Roles = "Admin,SuperAdmin")] // Restrict to admin roles
    public class UserController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly ILogger<UserController> _logger;

        public UserController(UserService userService, ILogger<UserController> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        // ==========================================
        // 1. CORE CRUD OPERATIONS
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> GetAllUsers([FromQuery] UserFilterRequest filter)
        {
            var users = await _userService.GetAllUsersAsync(filter);
            return Ok(users);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserAdminRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (success, message, userId) = await _userService.CreateUserAsync(request);
            if (!success) return BadRequest(new { message });

            return CreatedAtAction(nameof(GetUserById), new { userId }, new { message, userId });
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUserById(string userId)
        {
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound(new { message = "User not found" });

            return Ok(user);
        }

        [HttpPut("{userId}")]
        public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateUserAdminRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var success = await _userService.UpdateUserAsync(userId, request);
            if (!success) return NotFound(new { message = "User not found or update failed" });

            return Ok(new { message = "User updated successfully" });
        }

        [HttpPatch("{userId}")]
        public async Task<IActionResult> PatchUser(string userId, [FromBody] object request)
        {
            var success = await _userService.PatchUserAsync(userId, request);
            if (!success) return NotFound(new { message = "User not found, invalid payload, or patch failed" });

            return Ok(new { message = "User patched successfully" });
        }

        [HttpDelete("{userId}")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var success = await _userService.DeleteUserAsync(userId);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User deleted successfully" });
        }

        // ==========================================
        // 2. ACCOUNT STATUS MANAGEMENT
        // ==========================================

        [HttpPost("{userId}/activate")]
        public async Task<IActionResult> ActivateUser(string userId)
        {
            var success = await _userService.SetUserActiveStatusAsync(userId, isActive: true);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User account activated" });
        }

        [HttpPost("{userId}/deactivate")]
        public async Task<IActionResult> DeactivateUser(string userId)
        {
            var success = await _userService.SetUserActiveStatusAsync(userId, isActive: false);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User account deactivated" });
        }

        [HttpPost("{userId}/lock")]
        public async Task<IActionResult> LockUser(string userId)
        {
            var success = await _userService.SetUserLockStatusAsync(userId, isLocked: true);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User account locked" });
        }

        [HttpPost("{userId}/unlock")]
        public async Task<IActionResult> UnlockUser(string userId)
        {
            var success = await _userService.SetUserLockStatusAsync(userId, isLocked: false);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User account unlocked" });
        }

        // ==========================================
        // 3. SECURITY & VERIFICATION
        // ==========================================

        [HttpPost("{userId}/force-password-reset")]
        public async Task<IActionResult> ForcePasswordReset(string userId)
        {
            var success = await _userService.ForcePasswordResetAsync(userId);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User flagged for forced password reset. All active sessions have been killed." });
        }

        [HttpPost("{userId}/verify-email")]
        public async Task<IActionResult> ManuallyVerifyEmail(string userId)
        {
            var success = await _userService.VerifyUserEmailManuallyAsync(userId);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User email marked as verified" });
        }

        [HttpPost("{userId}/reset-mfa")]
        public async Task<IActionResult> ResetMfa(string userId)
        {
            var success = await _userService.ResetUserMfaAsync(userId);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "MFA has been reset for the user" });
        }

        [HttpPost("{userId}/revoke-sessions")]
        public async Task<IActionResult> RevokeUserSessions(string userId)
        {
            var success = await _userService.RevokeAllUserSessionsAsync(userId);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "All sessions revoked for the user" });
        }

        // ==========================================
        // 4. AUDIT & ACTIVITY
        // ==========================================

        [HttpGet("{userId}/sessions")]
        public async Task<IActionResult> GetUserSessions(string userId)
        {
            var sessions = await _userService.GetUserSessionsAsync(userId);
            if (sessions == null) return NotFound(new { message = "User not found" });

            // Returning safe session DTOs without raw Refresh Tokens
            var safeSessions = sessions.Select(s => new {
                id = s.SessionId,
                ip = s.IpAddress,
                device = s.DeviceInfo,
                createdAt = s.CreatedAt,
                expiry = s.Expiry
            });

            return Ok(new { sessions = safeSessions });
        }

        [HttpGet("{userId}/activity")]
        public async Task<IActionResult> GetUserActivity(string userId)
        {
            var activities = await _userService.GetUserActivityLogsAsync(userId);
            if (activities == null) return NotFound(new { message = "User not found or no logs available" });

            return Ok(new { activities });
        }
    }
}