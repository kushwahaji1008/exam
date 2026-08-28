using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EnrollmentService.Services;

namespace EnrollmentService.Controllers.V1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize] // Student must be logged in
    public class EnrollmentController : ControllerBase
    {
        private readonly EnrollmentManager _enrollmentManager;

        public EnrollmentController(EnrollmentManager enrollmentManager)
        {
            _enrollmentManager = enrollmentManager;
        }

        [HttpPost("buy")]
        public async Task<IActionResult> BuyCourse([FromBody] BuyCourseRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Note: In real app, fetch coursePrice from CourseService internally. 
            // For now, accepting from frontend for boilerplate.
            var result = await _enrollmentManager.PurchaseCourseAsync(userId, request.CourseId, request.Price);

            if (!result.Success) return BadRequest(new { message = result.Message });

            return Ok(new { message = result.Message });
        }

        [HttpGet("my-courses")]
        public async Task<IActionResult> GetMyCourses()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var courses = await _enrollmentManager.GetMyCoursesAsync(userId);
            return Ok(courses);
        }

        [HttpGet("check-access/{courseId}")]
        public async Task<IActionResult> CheckAccess(string courseId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var hasAccess = await _enrollmentManager.HasAccessAsync(userId, courseId);
            return Ok(new { hasAccess });
        }
    }

    public class BuyCourseRequest
    {
        public string CourseId { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}