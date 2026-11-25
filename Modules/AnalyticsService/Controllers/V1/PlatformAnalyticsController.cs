using AnalyticsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class PlatformAnalyticsController : ControllerBase
    {
        private readonly AnalyticsManagementService _analytics;

        public PlatformAnalyticsController(AnalyticsManagementService analytics)
        {
            _analytics = analytics;
        }

        [HttpGet("platform")]
        public async Task<IActionResult> GetPlatformOverview() =>
            Ok(await _analytics.GetPlatformOverviewAsync());

        [HttpGet("users")]
        public async Task<IActionResult> GetUserAnalytics() =>
            Ok(await _analytics.GetUserAnalyticsAsync());

        [HttpGet("exams")]
        public async Task<IActionResult> GetPlatformExamStats() =>
            Ok(await _analytics.GetPlatformExamStatsAsync());

        [HttpGet("attempts")]
        public async Task<IActionResult> GetAttemptAnalytics() =>
            Ok(await _analytics.GetAttemptAnalyticsAsync());

        [HttpGet("revenue")]
        public async Task<IActionResult> GetRevenueAnalytics() =>
            Ok(await _analytics.GetRevenueAnalyticsAsync());

        [HttpGet("activity")]
        public async Task<IActionResult> GetActivityHeatmap() =>
            Ok(await _analytics.GetActivityHeatmapAsync());
    }
}
