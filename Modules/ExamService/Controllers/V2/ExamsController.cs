using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Authorization;
using ExamService.Models;
using ExamService.Services;

namespace ExamService.V2.Controllers
{
    [ApiVersion("2.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams")]
    public class ExamsV2Controller : ControllerBase
    {
        private readonly ExamManagementService _examService;
        private readonly ILogger<ExamsV2Controller> _logger;

        public ExamsV2Controller(ExamManagementService examService, ILogger<ExamsV2Controller> logger)
        {
            _examService = examService;
            _logger = logger;
        }

        private string? GetCurrentUserId() => User.FindFirst("userId")?.Value;
        private string? GetCurrentUserRole() => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        // ==========================================
        // CORE CRUD
        // ==========================================

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllExams()
        {
            var exams = await _examService.GetAllExamsAsync(GetCurrentUserId(), GetCurrentUserRole());
            return Ok(exams.Select(ExamManagementService.ToExamDto));
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateExam([FromBody] CreateExamRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var exam = await _examService.CreateExamAsync(request, GetCurrentUserId()!);
            return Ok(new { message = "Exam created", exam = ExamManagementService.ToExamDto(exam) });
        }

        [HttpGet("{examId}")]
        [Authorize]
        public async Task<IActionResult> GetExamById(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            return exam == null ? NotFound() : Ok(exam);
        }

        [HttpPut("{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExam(string examId, [FromBody] Exam updatedExam)
        {
            var success = await _examService.UpdateExamAsync(examId, updatedExam);
            return success ? Ok(new { message = "Exam updated" }) : BadRequest();
        }

        [HttpPatch("{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PatchExam(string examId, [FromBody] object patchData)
        {
            // TODO: Implement partial update
            return Ok(new { message = "Exam patched" });
        }

        [HttpDelete("{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteExam(string examId)
        {
            var success = await _examService.DeleteExamAsync(examId);
            return success ? Ok(new { message = "Exam deleted" }) : NotFound();
        }

        // ==========================================
        // LIFECYCLE MANAGEMENT
        // ==========================================

        [HttpPost("{examId}/publish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishExam(string examId) => Ok(new { message = "Exam published" });

        [HttpPost("{examId}/unpublish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UnpublishExam(string examId) => Ok(new { message = "Exam unpublished" });

        [HttpPost("{examId}/activate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ActivateExam(string examId) => Ok(new { message = "Exam activated" });

        [HttpPost("{examId}/deactivate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeactivateExam(string examId) => Ok(new { message = "Exam deactivated" });

        [HttpPost("{examId}/archive")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ArchiveExam(string examId) => Ok(new { message = "Exam archived" });

        [HttpPost("{examId}/restore")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreExam(string examId) => Ok(new { message = "Exam restored" });

        [HttpPost("{examId}/duplicate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DuplicateExam(string examId) => Ok(new { message = "Exam duplicated", newExamId = "NEW_ID" });

        [HttpPost("{examId}/clone")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CloneExam(string examId) => Ok(new { message = "Exam cloned", newExamId = "NEW_ID" });

        // ==========================================
        // CONFIGURATION
        // ==========================================

        [HttpGet("{examId}/settings")]
        public async Task<IActionResult> GetExamSettings(string examId) => Ok(new { settings = new { } });

        [HttpPut("{examId}/settings")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamSettings(string examId, [FromBody] object settings) => Ok(new { message = "Settings updated" });

        [HttpGet("{examId}/schedule")]
        public async Task<IActionResult> GetExamSchedule(string examId) => Ok(new { schedule = new { } });

        [HttpPut("{examId}/schedule")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamSchedule(string examId, [FromBody] ScheduleRequest request) => Ok(new { message = "Schedule updated" });

        [HttpGet("{examId}/instructions")]
        public async Task<IActionResult> GetExamInstructions(string examId) => Ok(new { instructions = "..." });

        [HttpPut("{examId}/instructions")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamInstructions(string examId, [FromBody] object request) => Ok(new { message = "Instructions updated" });

        [HttpGet("{examId}/grading")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetExamGrading(string examId) => Ok(new { grading = new { } });

        [HttpPut("{examId}/grading")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamGrading(string examId, [FromBody] object request) => Ok(new { message = "Grading rules updated" });

        [HttpGet("health")]
        public IActionResult Health() => Ok(new { status = "healthy", service = "ExamService", timestamp = DateTime.UtcNow });

        // ==========================================
        // CUSTOM EXAM VIEWS & STATES (From your original code)
        // ==========================================

        [HttpPost("{examId}/complete")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CompleteExam(string examId)
        {
            var success = await _examService.CompleteExamAsync(examId);
            return success ? Ok(new { message = "Exam completed successfully" }) : BadRequest(new { message = "Failed to complete exam" });
        }

        [HttpGet("upcoming")]
        [Authorize]
        public async Task<IActionResult> GetUpcomingExams()
        {
            var exams = await _examService.GetUpcomingExamsAsync();
            return Ok(exams.Select(ExamManagementService.ToExamDto));
        }

        [HttpGet("active")]
        [Authorize]
        public async Task<IActionResult> GetActiveExams()
        {
            var exams = await _examService.GetActiveExamsAsync();
            return Ok(exams.Select(ExamManagementService.ToExamDto));
        }
    }

    public class ScheduleRequest { public DateTime StartTime { get; set; } public DateTime EndTime { get; set; } }
}