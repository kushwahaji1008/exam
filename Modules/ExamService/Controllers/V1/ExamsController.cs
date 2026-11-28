using System.Text.Json;
using ExamService.Models;
using ExamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ExamService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams")]
    public class ExamsController : ControllerBase
    {
        private readonly ExamManagementService _examService;
        private readonly ILogger<ExamsController> _logger;

        public ExamsController(ExamManagementService examService, ILogger<ExamsController> logger)
        {
            _examService = examService;
            _logger = logger;
        }

        private string? GetCurrentUserId() => User.FindFirst("userId")?.Value;
        private string? GetCurrentUserRole() =>
            User.FindAll(ClaimTypes.Role).Select(c => c.Value).FirstOrDefault(v => !int.TryParse(v, out _))
            ?? User.FindFirst(ClaimTypes.Role)?.Value;
        private bool IsStaff() => User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

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
            if (request.ScheduledStartTime.HasValue && request.ScheduledEndTime.HasValue &&
                request.ScheduledEndTime.Value < request.ScheduledStartTime.Value)
            {
                return BadRequest(new { message = "Scheduled end time must be after scheduled start time." });
            }
            if (request.TotalMarks > 0 && request.PassingMarks > request.TotalMarks)
            {
                return BadRequest(new { message = "Passing marks cannot exceed total marks." });
            }
            if (request.DiscountPrice > 0 && request.DiscountPrice > request.Price)
            {
                return BadRequest(new { message = "Discount price cannot exceed regular price." });
            }

            var exam = await _examService.CreateExamAsync(request, GetCurrentUserId() ?? "SYSTEM");
            return Ok(new { message = "Exam created", exam = ExamManagementService.ToExamDto(exam) });
        }

        [HttpGet("{examId}")]
        [Authorize]
        public async Task<IActionResult> GetExamById(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null) return NotFound();

            if (!IsStaff())
            {
                var userId = GetCurrentUserId() ?? string.Empty;
                bool isVisibleStatus = exam.Status is ExamStatus.Published or ExamStatus.Scheduled or ExamStatus.Active or ExamStatus.Completed;
                bool isBlocked = exam.BlockedStudents.Contains(userId);
                bool isAllowed = exam.AllowedStudents.Count == 0 || exam.AllowedStudents.Contains(userId);

                if (!isVisibleStatus || isBlocked || !isAllowed)
                    return Forbid();

                // Strip AllowedStudents / BlockedStudents candidate PII for students
                return Ok(new
                {
                    exam.Id,
                    exam.ExamId,
                    exam.CourseId,
                    exam.Title,
                    exam.Description,
                    exam.DurationMinutes,
                    exam.ScheduledStartTime,
                    exam.ScheduledEndTime,
                    exam.Type,
                    exam.Status,
                    exam.CreatedBy,
                    exam.CreatedAt,
                    exam.QuestionIds,
                    exam.Sections,
                    exam.Settings,
                    exam.Grading,
                    exam.InstructionsHtml,
                    exam.TotalMarks,
                    exam.PassingMarks,
                    exam.Price,
                    exam.DiscountPrice,
                    exam.Version
                });
            }

            return Ok(exam);
        }

        [HttpPut("{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExam(string examId, [FromBody] Exam updatedExam)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (updatedExam.ScheduledStartTime.HasValue && updatedExam.ScheduledEndTime.HasValue &&
                updatedExam.ScheduledEndTime.Value < updatedExam.ScheduledStartTime.Value)
            {
                return BadRequest(new { message = "Scheduled end time must be after scheduled start time." });
            }
            if (updatedExam.TotalMarks > 0 && updatedExam.PassingMarks > updatedExam.TotalMarks)
            {
                return BadRequest(new { message = "Passing marks cannot exceed total marks." });
            }
            if (updatedExam.DiscountPrice > 0 && updatedExam.DiscountPrice > updatedExam.Price)
            {
                return BadRequest(new { message = "Discount price cannot exceed regular price." });
            }

            var success = await _examService.UpdateExamAsync(examId, updatedExam);
            return success ? Ok(new { message = "Exam updated" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPatch("{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PatchExam(string examId, [FromBody] object patchData)
        {
            var success = await _examService.PatchExamAsync(examId, patchData);
            return success ? Ok(new { message = "Exam patched" }) : NotFound(new { message = "Exam not found or invalid patch payload" });
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
        public async Task<IActionResult> PublishExam(string examId)
        {
            var success = await _examService.ChangeExamStatusAsync(examId, ExamStatus.Published);
            return success ? Ok(new { message = "Exam published" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{examId}/unpublish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UnpublishExam(string examId)
        {
            var success = await _examService.ChangeExamStatusAsync(examId, ExamStatus.Draft);
            return success ? Ok(new { message = "Exam unpublished" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{examId}/activate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ActivateExam(string examId)
        {
            var success = await _examService.ActivateExamAsync(examId);
            return success ? Ok(new { message = "Exam activated" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{examId}/deactivate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeactivateExam(string examId)
        {
            var success = await _examService.ChangeExamStatusAsync(examId, ExamStatus.Published);
            return success ? Ok(new { message = "Exam deactivated" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{examId}/archive")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ArchiveExam(string examId)
        {
            var success = await _examService.ChangeExamStatusAsync(examId, ExamStatus.Archived);
            return success ? Ok(new { message = "Exam archived" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{examId}/restore")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreExam(string examId)
        {
            var success = await _examService.ChangeExamStatusAsync(examId, ExamStatus.Draft);
            return success ? Ok(new { message = "Exam restored" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{examId}/duplicate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DuplicateExam(string examId)
        {
            var newExamId = await _examService.CloneExamAsync(examId, GetCurrentUserId()!);
            return newExamId != null ? Ok(new { message = "Exam duplicated", newExamId }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{examId}/clone")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CloneExam(string examId)
        {
            var newExamId = await _examService.CloneExamAsync(examId, GetCurrentUserId()!);
            return newExamId != null ? Ok(new { message = "Exam cloned", newExamId }) : NotFound(new { message = "Exam not found" });
        }

        // ==========================================
        // CONFIGURATION
        // ==========================================

        [HttpGet("{examId}/settings")]
        [Authorize]
        public async Task<IActionResult> GetExamSettings(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            return exam == null ? NotFound() : Ok(new { settings = exam.Settings });
        }

        [HttpPut("{examId}/settings")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamSettings(string examId, [FromBody] ExamSettings settings)
        {
            var success = await _examService.UpdateExamSettingsAsync(examId, settings);
            return success ? Ok(new { message = "Settings updated" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpGet("{examId}/schedule")]
        [Authorize]
        public async Task<IActionResult> GetExamSchedule(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null) return NotFound();

            return Ok(new
            {
                schedule = new
                {
                    startTime = exam.ScheduledStartTime,
                    endTime = exam.ScheduledEndTime,
                    durationMinutes = exam.DurationMinutes
                }
            });
        }

        [HttpPut("{examId}/schedule")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamSchedule(string examId, [FromBody] ScheduleRequest request)
        {
            var success = await _examService.UpdateExamScheduleAsync(examId, request);
            return success ? Ok(new { message = "Schedule updated" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpGet("{examId}/instructions")]
        [Authorize]
        public async Task<IActionResult> GetExamInstructions(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            return exam == null ? NotFound() : Ok(new { instructions = exam.InstructionsHtml ?? string.Empty });
        }

        [HttpPut("{examId}/instructions")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamInstructions(string examId, [FromBody] JsonElement request)
        {
            string instructionsHtml = string.Empty;
            if (request.ValueKind == JsonValueKind.Object)
            {
                if (request.TryGetProperty("instructionsHtml", out var h)) instructionsHtml = h.GetString() ?? string.Empty;
                else if (request.TryGetProperty("instructions", out var i)) instructionsHtml = i.GetString() ?? string.Empty;
                else if (request.TryGetProperty("InstructionsHtml", out var p)) instructionsHtml = p.GetString() ?? string.Empty;
            }

            var success = await _examService.UpdateExamInstructionsAsync(examId, instructionsHtml);
            return success ? Ok(new { message = "Instructions updated" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpGet("{examId}/grading")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetExamGrading(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            return exam == null ? NotFound() : Ok(new { grading = exam.Grading });
        }

        [HttpPut("{examId}/grading")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateExamGrading(string examId, [FromBody] ExamGrading request)
        {
            var success = await _examService.UpdateExamGradingAsync(examId, request);
            return success ? Ok(new { message = "Grading rules updated" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpGet("health")]
        public IActionResult Health() => Ok(new { status = "healthy", service = "ExamService", timestamp = DateTime.UtcNow });

        // ==========================================
        // CUSTOM EXAM VIEWS & STATES
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

        // ==========================================
        // EXAM-ONLY SECTION CSV & RANDOM AUTO-GENERATION
        // ==========================================

        private const long MaxExamCsvBytes = 10 * 1024 * 1024; // 10 MB

        [HttpGet("import-questions-template")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public IActionResult DownloadExamSectionCsvTemplate()
        {
            var (bytes, contentType, fileName) = _examService.GetExamSectionCsvTemplate();
            return File(bytes, contentType, fileName);
        }

        [HttpPost("import-questions-csv")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxExamCsvBytes)]
        public async Task<IActionResult> ImportExamQuestionsCsv(
            IFormFile? file,
            [FromQuery] string? examId = null,
            [FromQuery] string? targetSectionTitle = null,
            [FromQuery] bool dryRun = false,
            [FromQuery] bool skipInvalidRows = true,
            [FromQuery] bool attachToExam = false)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Please upload a non-empty CSV file." });

            if (file.Length > MaxExamCsvBytes)
                return BadRequest(new { message = "CSV file exceeds the 10 MB upload limit." });

            if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Only .csv files are supported." });

            string csvContent;
            using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                csvContent = await reader.ReadToEndAsync();
            }

            if (csvContent.Contains('\0'))
                return BadRequest(new { message = "Invalid CSV file: binary content detected." });

            var result = await _examService.ImportExamCsvAsync(
                csvContent,
                GetCurrentUserId() ?? "SYSTEM",
                examId,
                targetSectionTitle,
                dryRun,
                skipInvalidRows,
                attachToExam);

            if (result.TotalRows == 0 && result.Errors.Count == 0)
                return BadRequest(result);

            if (!dryRun && ((!skipInvalidRows && result.Errors.Count > 0) || (result.ImportedCount == 0 && result.FailedCount > 0)))
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("auto-generate-questions")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> AutoGenerateExamQuestions([FromBody] AutoGenerateExamQuestionsRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var result = await _examService.AutoGenerateQuestionsAsync(request);
            if (!result.Success)
                return BadRequest(new { message = result.Message, result });

            return Ok(result);
        }

        [HttpPost("auto-create")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> AutoCreateExam([FromBody] AutoCreateExamRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message, exam, generation) = await _examService.AutoCreateExamAsync(request, GetCurrentUserId() ?? "SYSTEM");
            if (!success || exam == null)
                return BadRequest(new { message, generation });

            return Ok(new
            {
                message,
                exam = ExamManagementService.ToExamDto(exam),
                generation
            });
        }
    }
}
