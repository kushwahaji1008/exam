using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics")]
    [Authorize]
    public class DashboardsController : ControllerBase
    {
        [HttpGet("dashboard")]
        public IActionResult GetGeneralDashboard() => Ok(new { overview = "General stats" });

        [HttpGet("admin-dashboard")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public IActionResult GetAdminDashboard() => Ok(new { totalUsers = 1200, activeExams = 45, revenue = 8500 });

        [HttpGet("student-dashboard")]
        [Authorize(Roles = "Student")]
        public IActionResult GetStudentDashboard() => Ok(new { examsTaken = 12, averageScore = 78.5, upcomingExams = 2 });

        [HttpGet("instructor-dashboard")]
        [Authorize(Roles = "Teacher,Instructor")]
        public IActionResult GetInstructorDashboard() => Ok(new { coursesCreated = 5, totalStudentsEnrolled = 340 });
    }
}