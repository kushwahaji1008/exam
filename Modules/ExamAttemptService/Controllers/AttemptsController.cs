using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ExamAttemptService.Models;
using ExamAttemptService.Services;

namespace ExamAttemptService.Controllers
{
    [ApiController]
    [Route("api/attempts")]
    public class AttemptsController : ControllerBase
    {
        private readonly AttemptManagementService _attemptService;
        private readonly ILogger<AttemptsController> _logger;

        public AttemptsController(AttemptManagementService attemptService, ILogger<AttemptsController> logger)
        {
            _attemptService = attemptService;
            _logger = logger;
        }

        [HttpPost("start")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> StartExam([FromBody] StartExamRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            var attempt = await _attemptService.StartExamAsync(request.ExamId, userId, userName);

            return Ok(new { message = "Exam started", attempt });
        }

        [HttpGet("{attemptId}")]
        [Authorize]
        public async Task<IActionResult> GetAttempt(string attemptId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null)
            {
                return NotFound(new { message = "Attempt not found" });
            }

            // Students can only view their own attempts
            if (role == "Student" && attempt.StudentId != userId)
            {
                return Forbid();
            }

            return Ok(attempt);
        }

        [HttpGet("exam/{examId}/active")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetActiveAttempt(string examId)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var attempt = await _attemptService.GetActiveAttemptAsync(examId, userId);

            if (attempt == null)
            {
                return NotFound(new { message = "No active attempt found" });
            }

            return Ok(attempt);
        }

        [HttpGet("student/my-attempts")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyAttempts()
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var attempts = await _attemptService.GetStudentAttemptsAsync(userId);
            var attemptDtos = attempts.Select(AttemptManagementService.ToAttemptDto).ToList();

            return Ok(attemptDtos);
        }

        [HttpGet("exam/{examId}/all")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetExamAttempts(string examId)
        {
            var attempts = await _attemptService.GetExamAttemptsAsync(examId);
            var attemptDtos = attempts.Select(AttemptManagementService.ToAttemptDto).ToList();

            return Ok(attemptDtos);
        }

        [HttpPost("{attemptId}/answer")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> SaveAnswer(string attemptId, [FromBody] SubmitAnswerRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null)
            {
                return NotFound(new { message = "Attempt not found" });
            }

            if (attempt.StudentId != userId)
            {
                return Forbid();
            }

            if (attempt.Status != AttemptStatus.InProgress)
            {
                return BadRequest(new { message = "Cannot modify answers after submission" });
            }

            var answer = new Answer
            {
                QuestionId = request.QuestionId,
                SelectedOption = request.SelectedOption,
                SelectedOptions = request.SelectedOptions,
                TextAnswer = request.TextAnswer,
                CodeAnswer = request.CodeAnswer
            };

            var success = await _attemptService.SaveAnswerAsync(attemptId, answer);

            if (!success)
            {
                return BadRequest(new { message = "Failed to save answer" });
            }

            return Ok(new { message = "Answer saved successfully" });
        }

        [HttpPost("{attemptId}/flag/{questionId}")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> ToggleFlag(string attemptId, string questionId, [FromQuery] bool flagged = true)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null)
            {
                return NotFound(new { message = "Attempt not found" });
            }

            if (attempt.StudentId != userId)
            {
                return Forbid();
            }

            var success = await _attemptService.ToggleFlagAsync(attemptId, questionId, flagged);
            if (!success)
            {
                return BadRequest(new { message = "Failed to toggle flag" });
            }

            return Ok(new { message = "Flag updated" });
        }

        [HttpPost("{attemptId}/log")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> LogActivity(string attemptId, [FromBody] ActivityLogRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null)
            {
                return NotFound(new { message = "Attempt not found" });
            }

            if (attempt.StudentId != userId)
            {
                return Forbid();
            }

            var success = await _attemptService.LogActivityAsync(attemptId, request.Activity);
            if (!success)
            {
                return BadRequest(new { message = "Failed to log activity" });
            }

            return Ok(new { message = "Activity logged" });
        }

        [HttpPost("submit")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> SubmitExam([FromBody] SubmitExamRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(request.AttemptId);

            if (attempt == null)
            {
                return NotFound(new { message = "Attempt not found" });
            }

            if (attempt.StudentId != userId)
            {
                return Forbid();
            }

            if (attempt.Status != AttemptStatus.InProgress)
            {
                return BadRequest(new { message = "Exam already submitted" });
            }

            var success = await _attemptService.SubmitExamAsync(request.AttemptId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to submit exam" });
            }

            return Ok(new { message = "Exam submitted successfully" });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ExamAttemptService", timestamp = DateTime.UtcNow });
        }
    }

    public class ActivityLogRequest
    {
        public string Activity { get; set; } = string.Empty;
    }
}