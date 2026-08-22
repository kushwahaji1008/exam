using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AuthService.Models;
using AuthService.Services;
using System.Security.Claims;

namespace AuthService.V2.Controllers
{
    [ApiController]
    [Route("api/v2/auth")]
    public class AuthV2Controller : ControllerBase
    {
        private readonly AuthenticationService _authService;
        private readonly IPhoneService _phoneService;
        private readonly ILogger<AuthV2Controller> _logger;

        public AuthV2Controller(AuthenticationService authService, IPhoneService phoneService, ILogger<AuthV2Controller> logger)
        {
            _authService = authService;
            _phoneService = phoneService;
            _logger = logger;
        }

        // Helper to extract User ID from claims
        private string? GetCurrentUserId() =>
            User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // ==========================================
        // 1. AUTHENTICATION (Core)
        // ==========================================

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message) = await _authService.RegisterAsync(request);
            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message, token, refreshToken, userDto) = await _authService.LoginAsync(request);
            if (!success) return Unauthorized(new { message });
            return Ok(new LoginResponse { Token = token, RefreshToken = refreshToken, User = userDto! });
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _authService.LogoutAsync(userId);
            return Ok(new { message = "Logged out successfully" });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var (success, message, token, refreshToken, user) = await _authService.RefreshTokenAsync(request);
            if (!success) return Unauthorized(new { message });
            return Ok(new LoginResponse { Token = token, RefreshToken = refreshToken, User = user! });
        }

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyOtpRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message, token, refreshToken, userDto) = await _authService.VerifyOtpAsync(request);
            if (!success) return BadRequest(new { message });
            return Ok(new LoginResponse { Token = token, RefreshToken = refreshToken, User = userDto! });
        }

        [HttpPost("resend-otp")]
        public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest request)
        {
            var (success, message) = await _authService.ResendOtpAsync(request.Email, isPasswordReset: false);
            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ResendOtpRequest request)
        {
            var (success, message) = await _authService.ResendOtpAsync(request.Email, isPasswordReset: true);
            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var (success, message) = await _authService.ResetPasswordAsync(request);
            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var (success, message) = await _authService.ChangePasswordAsync(userId, request);
            return success ? Ok(new { message }) : BadRequest(new { message });
        }
        [HttpPost("check-email")]
        public async Task<IActionResult> CheckEmail([FromBody] CheckAvailabilityRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Value)) return BadRequest(new { message = "Email is required" });

            var exists = await _authService.CheckEmailExistsAsync(request.Value);
            return Ok(new { available = !exists });
        }

        [HttpPost("check-username")]
        public async Task<IActionResult> CheckUsername([FromBody] CheckAvailabilityRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Value)) return BadRequest(new { message = "Username is required" });

            var exists = await _authService.CheckUsernameExistsAsync(request.Value);
            return Ok(new { available = !exists });
        }

        [HttpPost("validate-token")]
        public IActionResult ValidateToken([FromBody] ValidateTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token)) return BadRequest(new { message = "Token is required" });

            var isValid = _authService.ValidateToken(request.Token);
            return Ok(new { valid = isValid });
        }

        [HttpPost("revoke-token")]
        [Authorize]
        public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _authService.RevokeTokenAsync(userId, request.Token);
            if (!success) return BadRequest(new { message = "Invalid token or token already revoked" });

            return Ok(new { message = "Token revoked successfully" });
        }

        [HttpPost("revoke-all-tokens")]
        [Authorize]
        public async Task<IActionResult> RevokeAllTokens()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _authService.RevokeAllTokensAsync(userId);
            return Ok(new { message = "All tokens revoked successfully" });
        }

        // ==========================================
        // 2. CURRENT USER (Me)
        // ==========================================

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetMe()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var userDto = await _authService.GetUserByIdAsync(userId);
            return userDto != null ? Ok(userDto) : NotFound();
        }

        [HttpPut("me")]
        [Authorize]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request)
        {
            var userId = GetCurrentUserId();
            var success = await _authService.UpdateUserProfileAsync(userId!, request);
            return success ? Ok(new { message = "Updated successfully" }) : BadRequest();
        }

        [HttpPatch("me")]
        [Authorize]
        public async Task<IActionResult> PatchMe([FromBody] object request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _authService.PatchCurrentUserAsync(userId, request);
            if (!success) return BadRequest(new { message = "Failed to update profile or invalid data provided." });

            return Ok(new { message = "Patched successfully" });
        }

        [HttpDelete("me")]
        [Authorize]
        public async Task<IActionResult> DeleteMe()
        {
            var userId = GetCurrentUserId();
            var success = await _authService.DeleteUserAsync(userId!);
            return success ? Ok(new { message = "Account deleted" }) : BadRequest();
        }

        [HttpGet("me/profile")]
        [Authorize]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = GetCurrentUserId();
            var userDto = await _authService.GetUserByIdAsync(userId!);
            return Ok(userDto);
        }

        [HttpPut("me/profile")]
        [Authorize]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = GetCurrentUserId();
            var success = await _authService.UpdateUserProfileAsync(userId!, request);
            return success ? Ok(new { message = "Profile updated" }) : BadRequest();
        }

        [HttpGet("me/security")]
        [Authorize]
        public async Task<IActionResult> GetMySecuritySettings()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var securitySettings = await _authService.GetSecuritySettingsAsync(userId);
            if (securitySettings == null) return NotFound(new { message = "User not found" });

            return Ok(securitySettings);
        }

        [HttpGet("me/activity")]
        [Authorize]
        public async Task<IActionResult> GetMyActivity([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var activities = await _authService.GetUserActivityAsync(userId, page, pageSize);
            return Ok(new { activities });
        }

        // ==========================================
        // 3. EMAIL MANAGEMENT
        // ==========================================

        [HttpPost("email/change")]
        [Authorize]
        public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var (success, message) = await _authService.ChangeEmailAsync(userId, request);
            if (!success) return BadRequest(new { message });

            return Ok(new { message });
        }

        [HttpPost("email/verify")]
        public async Task<IActionResult> VerifyEmailSpecific([FromBody] VerifyOtpRequest request)
        {
            // Reusing your core VerifyEmail method
            return await VerifyEmail(request);
        }

        [HttpPost("email/resend-verification")]
        public async Task<IActionResult> ResendEmailVerification([FromBody] ResendOtpRequest request)
        {
            // Reusing your core ResendOtp method
            return await ResendOtp(request);
        }

        // ==========================================
        // 4. PHONE MANAGEMENT
        // ==========================================

        [HttpPost("phone/add")]
        [Authorize]
        public async Task<IActionResult> AddPhone([FromBody] AddPhoneRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            var (success, message) = await _phoneService.AddPhoneAsync(userId!, request);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("phone/verify")]
        [Authorize]
        public async Task<IActionResult> VerifyPhone([FromBody] VerifyOtpRequest request)
        {
            var userId = GetCurrentUserId();
            // Note: Reusing VerifyOtpRequest. We only need the OTP field from it for phone verification
            var (success, message) = await _phoneService.VerifyPhoneAsync(userId!, request.Otp);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("phone/change")]
        [Authorize]
        public async Task<IActionResult> ChangePhone([FromBody] AddPhoneRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            var (success, message) = await _phoneService.ChangePhoneAsync(userId!, request);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpDelete("phone")]
        [Authorize]
        public async Task<IActionResult> RemovePhone()
        {
            var userId = GetCurrentUserId();
            var (success, message) = await _phoneService.RemovePhoneAsync(userId!);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("phone/resend-otp")]
        [Authorize]
        public async Task<IActionResult> ResendPhoneOtp()
        {
            var userId = GetCurrentUserId();
            var (success, message) = await _phoneService.ResendPhoneOtpAsync(userId!);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        // ==========================================
        // 5. MULTI-FACTOR AUTHENTICATION (MFA)
        // ==========================================

        [HttpGet("mfa")]
        [Authorize]
        public async Task<IActionResult> GetMfaInfo()
        {
            var userId = GetCurrentUserId();
            var info = await _authService.GetMfaInfoAsync(userId!);

            if (info == null) return NotFound(new { message = "User not found" });
            return Ok(info);
        }

        [HttpGet("mfa/status")]
        [Authorize]
        public async Task<IActionResult> GetMfaStatus()
        {
            var userId = GetCurrentUserId();
            var status = await _authService.GetMfaStatusAsync(userId!);

            return Ok(new { isEnabled = status });
        }

        [HttpPost("mfa/setup")]
        [Authorize]
        public async Task<IActionResult> SetupMfa([FromBody] MfaSetupRequest request)
        {
            var userId = GetCurrentUserId();
            var (success, message, secret, qrCodeUrl) = await _authService.SetupMfaAsync(userId!, request.Method);

            if (!success) return BadRequest(new { message });
            return Ok(new { secret, qrCodeUrl, method = request.Method });
        }

        [HttpPost("mfa/verify")]
        [Authorize]
        public async Task<IActionResult> VerifyMfaSetup([FromBody] MfaVerifyRequest request)
        {
            var userId = GetCurrentUserId();
            var (success, message) = await _authService.VerifyMfaSetupAsync(userId!, request.Code);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("mfa/enable")]
        [Authorize]
        public async Task<IActionResult> EnableMfa([FromBody] MfaVerifyRequest request)
        {
            var userId = GetCurrentUserId();
            var (success, message) = await _authService.EnableMfaAsync(userId!, request.Code);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("mfa/disable")]
        [Authorize]
        public async Task<IActionResult> DisableMfa([FromBody] MfaVerifyRequest request)
        {
            var userId = GetCurrentUserId();
            var (success, message) = await _authService.DisableMfaAsync(userId!, request.Code);

            return success ? Ok(new { message }) : BadRequest(new { message });
        }

        [HttpPost("mfa/challenge")]
        public async Task<IActionResult> MfaChallenge([FromBody] MfaChallengeRequest request)
        {
            // Note: Not [Authorize] because the user is currently trying to log in
            var (success, message, token, refreshToken, userDto) = await _authService.MfaChallengeAsync(request.UserId, request.Code);

            if (!success) return Unauthorized(new { message });
            return Ok(new LoginResponse { Token = token, RefreshToken = refreshToken, User = userDto! });
        }

        [HttpPost("mfa/recovery-codes")]
        [Authorize]
        public async Task<IActionResult> GetRecoveryCodes()
        {
            var userId = GetCurrentUserId();
            var codes = await _authService.GetRecoveryCodesAsync(userId!);

            if (codes == null || !codes.Any()) return BadRequest(new { message = "MFA is not enabled or codes not found." });
            return Ok(new { codes });
        }

        [HttpPost("mfa/recovery-codes/regenerate")]
        [Authorize]
        public async Task<IActionResult> RegenerateRecoveryCodes()
        {
            var userId = GetCurrentUserId();
            var (success, message, codes) = await _authService.RegenerateRecoveryCodesAsync(userId!);

            if (!success) return BadRequest(new { message });
            return Ok(new { codes });
        }

        [HttpPost("mfa/recovery-codes/verify")]
        public async Task<IActionResult> VerifyRecoveryCode([FromBody] MfaChallengeRequest request)
        {
            // Note: Also used during login, so no [Authorize] tag
            var (success, message, token, refreshToken, userDto) = await _authService.VerifyRecoveryCodeAsync(request.UserId, request.Code);

            if (!success) return Unauthorized(new { message });
            return Ok(new LoginResponse { Token = token, RefreshToken = refreshToken, User = userDto! });
        }

        // ==========================================
        // 6. SESSIONS & DEVICES
        // ==========================================

        [HttpGet("sessions")]
        [Authorize]
        public async Task<IActionResult> GetSessions()
        {
            var userId = GetCurrentUserId();
            var sessions = await _authService.GetUserSessionsAsync(userId!);

            // Map to anonymous object to avoid exposing the raw RefreshToken
            var sessionDtos = sessions.Select(s => new
            {
                id = s.SessionId,
                ip = s.IpAddress,
                device = s.DeviceInfo,
                createdAt = s.CreatedAt,
                expiry = s.Expiry
            });

            return Ok(new { sessions = sessionDtos });
        }

        [HttpGet("sessions/{sessionId}")]
        [Authorize]
        public async Task<IActionResult> GetSessionById(string sessionId)
        {
            var userId = GetCurrentUserId();
            var session = await _authService.GetSessionByIdAsync(userId!, sessionId);

            if (session == null) return NotFound(new { message = "Session not found" });

            return Ok(new
            {
                id = session.SessionId,
                ip = session.IpAddress,
                device = session.DeviceInfo,
                createdAt = session.CreatedAt,
                expiry = session.Expiry
            });
        }

        [HttpDelete("sessions/{sessionId}")]
        [Authorize]
        public async Task<IActionResult> DeleteSession(string sessionId)
        {
            var userId = GetCurrentUserId();
            var success = await _authService.RevokeSessionAsync(userId!, sessionId);

            return success ? Ok(new { message = "Session deleted" }) : NotFound(new { message = "Session not found" });
        }

        [HttpPost("sessions/{sessionId}/revoke")]
        [Authorize]
        public async Task<IActionResult> RevokeSession(string sessionId)
        {
            var userId = GetCurrentUserId();
            var success = await _authService.RevokeSessionAsync(userId!, sessionId);

            return success ? Ok(new { message = "Session revoked" }) : NotFound(new { message = "Session not found" });
        }

        [HttpPost("sessions/revoke-all")]
        [Authorize]
        public async Task<IActionResult> RevokeAllSessions()
        {
            var userId = GetCurrentUserId();

            // Note: This clears ALL sessions. The current user will be required to log in again.
            await _authService.RevokeAllTokensAsync(userId!);
            return Ok(new { message = "All sessions revoked successfully. Please log in again." });
        }

        [HttpGet("devices")]
        [Authorize]
        public async Task<IActionResult> GetDevices()
        {
            var userId = GetCurrentUserId();
            var sessions = await _authService.GetUserSessionsAsync(userId!);

            var devices = sessions.Select(s => new
            {
                deviceId = s.SessionId, // Mapping SessionId as DeviceId
                name = s.DeviceInfo ?? "Unknown Device",
                lastIp = s.IpAddress,
                lastActive = s.CreatedAt
            });

            return Ok(new { devices });
        }

        [HttpGet("devices/{deviceId}")]
        [Authorize]
        public async Task<IActionResult> GetDeviceById(string deviceId)
        {
            var userId = GetCurrentUserId();
            var session = await _authService.GetSessionByIdAsync(userId!, deviceId);

            if (session == null) return NotFound(new { message = "Device not found" });

            return Ok(new
            {
                id = session.SessionId,
                name = session.DeviceInfo ?? "Unknown Device",
                lastIp = session.IpAddress,
                lastActive = session.CreatedAt
            });
        }

        [HttpDelete("devices/{deviceId}")]
        [Authorize]
        public async Task<IActionResult> RemoveDevice(string deviceId)
        {
            var userId = GetCurrentUserId();
            var success = await _authService.RevokeSessionAsync(userId!, deviceId);

            return success ? Ok(new { message = "Device removed" }) : NotFound(new { message = "Device not found" });
        }

        // ==========================================
        // 7. ADMIN / SYSTEM (From existing code)
        // ==========================================

        // [HttpGet("users")]
        // [Authorize(Roles = "1,2,Admin,SuperAdmin")]
        // public async Task<IActionResult> GetAllUsers()
        // {
        //     var userDtos = await _authService.GetAllUsersAsync();
        //     return Ok(userDtos);
        // }

        // [HttpGet("users/{userId}")]
        // [Authorize]
        // public async Task<IActionResult> GetUserById(string userId)
        // {
        //     var userDto = await _authService.GetUserByIdAsync(userId);
        //     return userDto != null ? Ok(userDto) : NotFound(new { message = "User not found" });
        // }

        // [HttpPut("users/{userId}")]
        // [Authorize]
        // public async Task<IActionResult> AdminUpdateUser(string userId, [FromBody] UpdateProfileRequest request)
        // {
        //     var currentUserId = GetCurrentUserId();
        //     var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;

        //     if (currentUserId != userId && currentUserRole != "1" && currentUserRole != "2" && currentUserRole != "Admin" && currentUserRole != "SuperAdmin")
        //     {
        //         return Forbid();
        //     }

        //     var success = await _authService.UpdateUserProfileAsync(userId, request);
        //     return success ? Ok(new { message = "Profile updated successfully" }) : NotFound(new { message = "User not found or no changes were made" });
        // }

        // [HttpDelete("users/{userId}")]
        // [Authorize(Roles = "1,2,Admin,SuperAdmin")]
        // public async Task<IActionResult> DeleteUserById(string userId)
        // {
        //     var success = await _authService.DeleteUserAsync(userId);
        //     return success ? Ok(new { message = "User deleted successfully" }) : NotFound(new { message = "User not found" });
        // }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "AuthService", timestamp = DateTime.UtcNow });
        }
    }
}
