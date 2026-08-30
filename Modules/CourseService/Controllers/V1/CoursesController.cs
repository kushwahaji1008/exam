using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CourseService.Models.V1;
using CourseService.Services;

namespace CourseService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/courses")]
    public class CoursesController : ControllerBase
    {
        private readonly CourseManagementService _courseService;

        public CoursesController(CourseManagementService courseService)
        {
            _courseService = courseService;
        }

        private string? GetCurrentUserId() => User.FindFirst("userId")?.Value;

        // ==========================================
        // 1. CORE CRUD & LIFECYCLE
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> GetAllCourses()
        {
            var courses = await _courseService.GetAllCoursesAsync();
            return Ok(courses);
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateCourse([FromBody] CreateCourseRequest request)
        {
            var course = await _courseService.CreateCourseAsync(request, GetCurrentUserId()!);
            return CreatedAtAction(nameof(GetCourseById), new { courseId = course.CourseId }, course);
        }

        [HttpGet("{courseId}")]
        public async Task<IActionResult> GetCourseById(string courseId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            return course == null ? NotFound() : Ok(course);
        }

        [HttpPatch("{courseId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateCourse(string courseId, [FromBody] object request)
        {
            var success = await _courseService.PatchCourseAsync(courseId, request);
            return success ? Ok(new { message = "Course updated" }) : BadRequest();
        }

        [HttpDelete("{courseId}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteCourse(string courseId)
        {
            var success = await _courseService.DeleteCourseAsync(courseId);
            return success ? Ok(new { message = "Course deleted" }) : NotFound();
        }

        [HttpPost("{courseId}/publish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishCourse(string courseId)
        {
            var success = await _courseService.ChangeCourseStatusAsync(courseId, CourseStatus.Published);
            return success ? Ok(new { message = "Course published" }) : BadRequest();
        }

        [HttpPost("{courseId}/unpublish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UnpublishCourse(string courseId)
        {
            var success = await _courseService.ChangeCourseStatusAsync(courseId, CourseStatus.Draft);
            return success ? Ok(new { message = "Course unpublished" }) : BadRequest();
        }

        // ==========================================
        // 2. DISCOVERY (Public)
        // ==========================================

        [HttpGet("featured")]
        public IActionResult GetFeaturedCourses() => Ok(new { courses = new List<object>() });

        [HttpGet("popular")]
        public IActionResult GetPopularCourses() => Ok(new { courses = new List<object>() });

        [HttpGet("recommended")]
        public IActionResult GetRecommendedCourses() => Ok(new { courses = new List<object>() });

        [HttpGet("search")]
        public IActionResult SearchCourses([FromQuery] string query) => Ok(new { results = new List<object>(), query });

        // ==========================================
        // 3. STUDENT PORTAL
        // ==========================================

        [HttpGet("{courseId}/overview")]
        public IActionResult GetCourseOverview(string courseId) => Ok(new { syllabus = "...", prerequisites = "..." });

        [HttpGet("{courseId}/dashboard")]
        [Authorize]
        public IActionResult GetStudentDashboard(string courseId) => Ok(new { progress = 45, nextItem = "Item2" });

        [HttpGet("{courseId}/access")]
        [Authorize]
        public IActionResult CheckCourseAccess(string courseId) => Ok(new { hasAccess = true, role = "Student" });

        // ==========================================
        // 4. ADMIN & ANALYTICS
        // ==========================================

        [HttpGet("{courseId}/stats")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public IActionResult GetCourseStats(string courseId) => Ok(new { enrolledStudents = 120, totalRevenue = 4500, averageRating = 4.8 });
    }
}