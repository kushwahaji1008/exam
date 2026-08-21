using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AuthService.Models;
using AuthService.Services;
using System.Security.Claims;

namespace AuthService.V1.Controllers
{
    [ApiController]
    [Route("api/v1/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthenticationService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AuthenticationService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        // ==========================================
        // 1. REGISTRATION & VERIFICATION
        // ==========================================
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var (success, message) = await _authService.RegisterAsync(request);
            if (!success) return BadRequest(new { message });

            return Ok(new { message });
        }

        [HttpPost("verify-email")] // Can also be mapped to "verify-otp"
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
            if (!success) return BadRequest(new { message });
            return Ok(new { message });
        }

        // ==========================================
        // 2. LOGIN & LOGOUT
        // ==========================================
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
            var userId = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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

        // ==========================================
        // 3. PASSWORD MANAGEMENT
        // ==========================================
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ResendOtpRequest request)
        {
            var (success, message) = await _authService.ResendOtpAsync(request.Email, isPasswordReset: true);
            if (!success) return BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var (success, message) = await _authService.ResetPasswordAsync(request);
            if (!success) return BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var (success, message) = await _authService.ChangePasswordAsync(userId, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { message });
        }

        // ==========================================
        // 4. USER PROFILE MANAGEMENT
        // ==========================================
        [HttpGet("users")]
        [Authorize(Roles = "1,2,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllUsers()
        {
            var userDtos = await _authService.GetAllUsersAsync();
            return Ok(userDtos);
        }

        [HttpGet("users/{userId}")]
        [Authorize]
        public async Task<IActionResult> GetUserById(string userId)
        {
            var userDto = await _authService.GetUserByIdAsync(userId);
            if (userDto == null) return NotFound(new { message = "User not found" });
            return Ok(userDto);
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userId = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized(new { message = "Invalid token" });

            var userDto = await _authService.GetUserByIdAsync(userId);
            if (userDto == null) return NotFound(new { message = "User not found" });

            return Ok(userDto);
        }

        [HttpPut("users/{userId}")]
        [Authorize]
        public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateProfileRequest request)
        {
            var currentUserId = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (currentUserId != userId && currentUserRole != "1" && currentUserRole != "2"&& currentUserRole != "Admin" && currentUserRole != "SuperAdmin")
            {
                return Forbid();
            }

            // Using the secure partial update method!
            var success = await _authService.UpdateUserProfileAsync(userId, request);
            if (!success) return NotFound(new { message = "User not found or no changes were made" });

            return Ok(new { message = "Profile updated successfully" });
        }

        [HttpDelete("users/{userId}")]
        [Authorize(Roles = "1,2,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var success = await _authService.DeleteUserAsync(userId);
            if (!success) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User deleted successfully" });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "AuthService", timestamp = DateTime.UtcNow });
        }
    }
}