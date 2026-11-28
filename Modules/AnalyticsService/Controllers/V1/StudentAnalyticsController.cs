using System.Security.Claims;
using AnalyticsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics/users/{userId}")]
    [Authorize]
    public class StudentAnalyticsController : ControllerBase
    {
        private readonly AnalyticsManagementService _analytics;

        public StudentAnalyticsController(AnalyticsManagementService analytics)
        {
            _analytics = analytics;
        }

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private bool CanAccessUser(string userId) =>
            GetUserId() == userId || User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        [HttpGet]
        public async Task<IActionResult> GetUserSummary(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();

        [HttpGet("performance")]
        public async Task<IActionResult> GetPerformance(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();

        [HttpGet("progress")]
        public async Task<IActionResult> GetProgress(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();

        [HttpGet("accuracy")]
        public async Task<IActionResult> GetAccuracy(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();

        [HttpGet("attempts")]
        public async Task<IActionResult> GetAttemptsHistory(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();

        [HttpGet("time-spent")]
        public async Task<IActionResult> GetTimeSpent(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();

        [HttpGet("strengths")]
        public async Task<IActionResult> GetStrengths(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();

        [HttpGet("weaknesses")]
        public async Task<IActionResult> GetWeaknesses(string userId) =>
            CanAccessUser(userId) ? Ok(await _analytics.GetStudentAnalyticsAsync(userId)) : Forbid();
    }
}
