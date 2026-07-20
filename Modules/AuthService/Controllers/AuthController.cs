using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AuthService.Models;
using AuthService.Services;

namespace AuthService.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthenticationService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AuthenticationService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var (success, message, user) = await _authService.RegisterAsync(request);

            if (!success)
            {
                return BadRequest(new { message });
            }

            var userDto = AuthenticationService.ToUserDto(user!);
            return Ok(new { message, user = userDto });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var (success, message, token, user) = await _authService.LoginAsync(request);

            if (!success)
            {
                return Unauthorized(new { message });
            }

            var userDto = AuthenticationService.ToUserDto(user!);
            return Ok(new LoginResponse { Token = token, User = userDto });
        }

        [HttpGet("users")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _authService.GetAllUsersAsync();
            var userDtos = users.Select(AuthenticationService.ToUserDto).ToList();
            return Ok(userDtos);
        }

        [HttpGet("users/{userId}")]
        [Authorize]
        public async Task<IActionResult> GetUserById(string userId)
        {
            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var userDto = AuthenticationService.ToUserDto(user);
            return Ok(userDto);
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Invalid token" });
            }

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            var userDto = AuthenticationService.ToUserDto(user);
            return Ok(userDto);
        }

        [HttpPut("users/{userId}")]
        [Authorize]
        public async Task<IActionResult> UpdateUser(string userId, [FromBody] User updatedUser)
        {
            var currentUserId = User.FindFirst("userId")?.Value;
            var currentUserRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            // Users can only update their own profile unless they are admin
            if (currentUserId != userId && currentUserRole != "Admin" && currentUserRole != "SuperAdmin")
            {
                return Forbid();
            }

            var success = await _authService.UpdateUserAsync(userId, updatedUser);
            if (!success)
            {
                return NotFound(new { message = "User not found or update failed" });
            }

            return Ok(new { message = "User updated successfully" });
        }

        [HttpDelete("users/{userId}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var success = await _authService.DeleteUserAsync(userId);
            if (!success)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(new { message = "User deleted successfully" });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "AuthService", timestamp = DateTime.UtcNow });
        }
    }
}
