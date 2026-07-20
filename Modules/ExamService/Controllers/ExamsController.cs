using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ExamService.Models;
using ExamService.Services;

namespace ExamService.Controllers
{
    [ApiController]
    [Route("api/exams")]
    public class ExamsController : ControllerBase
    {
        private readonly ExamManagementService _examService;
        private readonly ILogger<ExamsController> _logger;

        public ExamsController(ExamManagementService examService, ILogger<ExamsController> logger)
        {
            _examService = examService;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateExam([FromBody] CreateExamRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var exam = await _examService.CreateExamAsync(request, userId);

            return Ok(new { message = "Exam created successfully", exam = ExamManagementService.ToExamDto(exam) });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllExams()
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var exams = await _examService.GetAllExamsAsync(userId, role);
            var examDtos = exams.Select(ExamManagementService.ToExamDto).ToList();

            return Ok(examDtos);
        }

        [HttpGet("{examId}")]
        [Authorize]
        public async Task<IActionResult> GetExamById(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null)
            {
                return NotFound(new { message = "Exam not found" });
            }

            return Ok(exam);
        }

        [HttpPut("{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExam(string examId, [FromBody] Exam updatedExam)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null)
            {
                return NotFound(new { message = "Exam not found" });
            }

            // Only creator or admin can update
            if (exam.CreatedBy != userId && role != "Admin" && role != "SuperAdmin")
            {
                return Forbid();
            }

            updatedExam.Id = examId;
            var success = await _examService.UpdateExamAsync(examId, updatedExam);

            if (!success)
            {
                return BadRequest(new { message = "Failed to update exam" });
            }

            return Ok(new { message = "Exam updated successfully" });
        }

        [HttpDelete("{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteExam(string examId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null)
            {
                return NotFound(new { message = "Exam not found" });
            }

            // Only creator or admin can delete
            if (exam.CreatedBy != userId && role != "Admin" && role != "SuperAdmin")
            {
                return Forbid();
            }

            var success = await _examService.DeleteExamAsync(examId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to delete exam" });
            }

            return Ok(new { message = "Exam deleted successfully" });
        }

        [HttpPost("{examId}/schedule")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ScheduleExam(string examId, [FromBody] ScheduleRequest request)
        {
            var success = await _examService.ScheduleExamAsync(examId, request.StartTime);
            if (!success)
            {
                return BadRequest(new { message = "Failed to schedule exam" });
            }

            return Ok(new { message = "Exam scheduled successfully" });
        }

        [HttpPost("{examId}/activate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ActivateExam(string examId)
        {
            var success = await _examService.ActivateExamAsync(examId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to activate exam" });
            }

            return Ok(new { message = "Exam activated successfully" });
        }

        [HttpPost("{examId}/complete")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CompleteExam(string examId)
        {
            var success = await _examService.CompleteExamAsync(examId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to complete exam" });
            }

            return Ok(new { message = "Exam completed successfully" });
        }

        [HttpGet("upcoming")]
        [Authorize]
        public async Task<IActionResult> GetUpcomingExams()
        {
            var exams = await _examService.GetUpcomingExamsAsync();
            var examDtos = exams.Select(ExamManagementService.ToExamDto).ToList();

            return Ok(examDtos);
        }

        [HttpGet("active")]
        [Authorize]
        public async Task<IActionResult> GetActiveExams()
        {
            var exams = await _examService.GetActiveExamsAsync();
            var examDtos = exams.Select(ExamManagementService.ToExamDto).ToList();

            return Ok(examDtos);
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ExamService", timestamp = DateTime.UtcNow });
        }
    }

    public class ScheduleRequest
    {
        public DateTime StartTime { get; set; }
    }
}
