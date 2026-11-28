using System.Security.Claims;
using AnalyticsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics")]
    [Authorize]
    public class DashboardsController : ControllerBase
    {
        private readonly AnalyticsManagementService _analytics;

        public DashboardsController(AnalyticsManagementService analytics)
        {
            _analytics = analytics;
        }

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private string? GetRole() =>
            User.FindAll(ClaimTypes.Role).Select(c => c.Value).FirstOrDefault(v => !int.TryParse(v, out _))
            ?? User.FindFirst(ClaimTypes.Role)?.Value;

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetGeneralDashboard() =>
            Ok(await _analytics.GetGeneralDashboardAsync(GetUserId(), GetRole()));

        [HttpGet("admin-dashboard")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetAdminDashboard() =>
            Ok(await _analytics.GetAdminDashboardAsync());

        [HttpGet("student-dashboard")]
        [Authorize(Roles = "Student,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetStudentDashboard() =>
            Ok(await _analytics.GetStudentDashboardAsync(GetUserId() ?? string.Empty));

        [HttpGet("instructor-dashboard")]
        [Authorize(Roles = "Teacher,Instructor,Admin,SuperAdmin")]
        public async Task<IActionResult> GetInstructorDashboard() =>
            Ok(await _analytics.GetInstructorDashboardAsync(GetUserId() ?? string.Empty));
    }
}
