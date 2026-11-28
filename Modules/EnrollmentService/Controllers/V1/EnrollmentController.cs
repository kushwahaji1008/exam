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
            var result = await _enrollmentManager.PurchaseCourseAsync(userId, request.CourseId);

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

        [HttpPost("buy-exam")]
        public async Task<IActionResult> BuyExam([FromBody] BuyExamRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _enrollmentManager.PurchaseExamAsync(userId, request.ExamId);
            if (!result.Success) return BadRequest(new { message = result.Message });
            return Ok(new { message = result.Message });
        }

        [HttpGet("my-exams")]
        public async Task<IActionResult> GetMyExams()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var exams = await _enrollmentManager.GetMyExamsAsync(userId);
            return Ok(exams);
        }

        [HttpGet("check-exam-access/{examId}")]
        public async Task<IActionResult> CheckExamAccess(string examId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var (hasAccess, isPurchased, price, discountPrice, effectivePrice) =
                await _enrollmentManager.CheckExamAccessAsync(userId, examId);
            return Ok(new { hasAccess, isPurchased, price, discountPrice, effectivePrice });
        }

        [HttpPost("buy-series")]
        public async Task<IActionResult> BuySeries([FromBody] BuySeriesRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _enrollmentManager.PurchaseSeriesAsync(userId, request.SeriesId);
            if (!result.Success) return BadRequest(new { message = result.Message });
            return Ok(new { message = result.Message });
        }

        [HttpGet("my-series")]
        public async Task<IActionResult> GetMySeries()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var series = await _enrollmentManager.GetMySeriesAsync(userId);
            return Ok(series);
        }

        [HttpGet("check-series-access/{seriesId}")]
        public async Task<IActionResult> CheckSeriesAccess(string seriesId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var hasAccess = await _enrollmentManager.HasSeriesAccessAsync(userId, seriesId);
            return Ok(new { hasAccess });
        }
    }

    public class BuyCourseRequest
    {
        public string CourseId { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public class BuyExamRequest
    {
        public string ExamId { get; set; } = string.Empty;
    }

    public class BuySeriesRequest
    {
        public string SeriesId { get; set; } = string.Empty;
    }
}