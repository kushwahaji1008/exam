using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class PlatformAnalyticsController : ControllerBase
    {
        [HttpGet("platform")]
        public IActionResult GetPlatformOverview() => Ok(new { uptime = "99.9%", activeConnections = 450 });

        [HttpGet("users")]
        public IActionResult GetUserAnalytics() => Ok(new { totalRegistrations = 5000, activeToday = 1200 });

        [HttpGet("exams")]
        public IActionResult GetPlatformExamStats() => Ok(new { totalExamsConducted = 350 });

        [HttpGet("attempts")]
        public IActionResult GetAttemptAnalytics() => Ok(new { totalAttempts = 45000 });

        [HttpGet("revenue")]
        public IActionResult GetRevenueAnalytics() => Ok(new { mrr = 25000, currency = "USD" });

        [HttpGet("activity")]
        public IActionResult GetActivityHeatmap() => Ok(new { peakHours = "10 AM - 2 PM" });
    }
}