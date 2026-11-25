using System.Security.Claims;
using ExamAttemptService.Models;
using ExamAttemptService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamAttemptService.Controllers
{
    [ApiController]
    [Route("api/v1/attempts")]
    public class AttemptsController : ControllerBase
    {
        private readonly AttemptManagementService _attemptService;
        private readonly ILogger<AttemptsController> _logger;

        public AttemptsController(AttemptManagementService attemptService, ILogger<AttemptsController> logger)
        {
            _attemptService = attemptService;
            _logger = logger;
        }

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private string? GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value;
        private bool IsStaff() => GetUserRole() is "Teacher" or "Admin" or "SuperAdmin";

        #region 1. Attempts (CRUD)

        [HttpGet]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllAttempts()
        {
            var attempts = await _attemptService.GetAllAttemptsAsync();
            return Ok(attempts.Select(AttemptManagementService.ToAttemptDto));
        }

        [HttpPost]
        [Authorize(Roles = "Student,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateAttempt([FromBody] StartExamRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

            var (success, message, attempt) = await _attemptService.CreateAndValidateAttemptAsync(request.ExamId, userId, userName);
            if (!success || attempt == null)
                return BadRequest(new { message });

            return Ok(new { message, attempt });
        }

        [HttpGet("{attemptId}")]
        [Authorize]
        public async Task<IActionResult> GetAttempt(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });

            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();
            return Ok(attempt);
        }

        [HttpDelete("{attemptId}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteAttempt(string attemptId)
        {
            var deleted = await _attemptService.DeleteAttemptAsync(attemptId);
            return deleted ? Ok(new { message = $"Attempt {attemptId} deleted successfully" }) : NotFound(new { message = "Attempt not found" });
        }

        #endregion

        #region 2. Start/End Lifecycle

        [HttpPost("{attemptId}/start")]
        [Authorize]
        public async Task<IActionResult> StartExam(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var ok = await _attemptService.MarkAttemptStartedAsync(attemptId);
            return ok ? Ok(new { message = "Exam session started", startedAt = attempt.StartedAt }) : BadRequest(new { message = "Attempt cannot be started" });
        }

        [HttpPost("{attemptId}/pause")]
        [Authorize]
        public async Task<IActionResult> PauseAttempt(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var success = await _attemptService.PauseAttemptAsync(attemptId);
            return success ? Ok(new { message = "Exam paused" }) : BadRequest(new { message = "Attempt is not currently in progress" });
        }

        [HttpPost("{attemptId}/resume")]
        [Authorize]
        public async Task<IActionResult> ResumeAttempt(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var success = await _attemptService.ResumeAttemptAsync(attemptId);
            return success ? Ok(new { message = "Exam resumed" }) : BadRequest(new { message = "Attempt is not paused" });
        }

        [HttpPost("{attemptId}/submit")]
        [Authorize]
        public async Task<IActionResult> SubmitExam(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId() && !IsStaff()) return Forbid();
            if (attempt.Status is not (AttemptStatus.InProgress or AttemptStatus.Paused))
                return BadRequest(new { message = "Exam already submitted or not in progress" });

            var success = await _attemptService.SubmitExamAsync(attemptId);
            if (!success) return BadRequest(new { message = "Failed to submit exam" });

            var updated = await _attemptService.GetAttemptByIdAsync(attemptId);
            return Ok(new { message = "Exam submitted successfully", attempt = updated != null ? AttemptManagementService.ToAttemptDto(updated) : null });
        }

        [HttpPost("{attemptId}/force-submit")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ForceSubmitExam(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });

            var success = await _attemptService.SubmitExamAsync(attemptId, autoSubmit: true);
            return success ? Ok(new { message = "Exam force-submitted" }) : BadRequest(new { message = "Attempt already in a terminal state" });
        }

        [HttpPost("{attemptId}/terminate")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> TerminateExam(string attemptId, [FromQuery] string? reason = null)
        {
            var success = await _attemptService.TerminateAttemptAsync(attemptId, reason ?? "Terminated by proctor/admin");
            return success ? Ok(new { message = "Exam terminated" }) : NotFound(new { message = "Attempt not found" });
        }

        #endregion

        #region 3. Questions & Answers

        [HttpGet("{attemptId}/questions")]
        [Authorize]
        public async Task<IActionResult> GetQuestions(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var (_, questions) = await _attemptService.GetAttemptQuestionsAsync(attemptId);
            return Ok(questions);
        }

        [HttpGet("{attemptId}/questions/{questionId}")]
        [Authorize]
        public async Task<IActionResult> GetQuestion(string attemptId, string questionId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var question = await _attemptService.GetAttemptQuestionAsync(attemptId, questionId);
            return question != null ? Ok(question) : NotFound(new { message = "Question not found in this attempt" });
        }

        [HttpPost("{attemptId}/questions/{questionId}/answer")]
        [Authorize]
        public async Task<IActionResult> SaveAnswer(string attemptId, string questionId, [FromBody] SubmitAnswerRequest request)
        {
            var userId = GetUserId();
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != userId) return Forbid();
            if (attempt.Status != AttemptStatus.InProgress)
                return BadRequest(new { message = "Cannot modify answers after submission" });

            var answer = new Answer
            {
                QuestionId = questionId,
                SelectedOption = request.SelectedOption,
                SelectedOptions = request.SelectedOptions ?? new List<string>(),
                TextAnswer = request.TextAnswer,
                CodeAnswer = request.CodeAnswer
            };

            var success = await _attemptService.SaveAnswerAsync(attemptId, answer);
            if (!success) return BadRequest(new { message = "Failed to save answer" });

            return Ok(new { message = "Answer saved successfully" });
        }

        [HttpPut("{attemptId}/questions/{questionId}/answer")]
        [Authorize]
        public Task<IActionResult> UpdateAnswer(string attemptId, string questionId, [FromBody] SubmitAnswerRequest request)
            => SaveAnswer(attemptId, questionId, request);

        [HttpDelete("{attemptId}/questions/{questionId}/answer")]
        [Authorize]
        public async Task<IActionResult> ClearAnswer(string attemptId, string questionId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId()) return Forbid();
            if (attempt.Status != AttemptStatus.InProgress)
                return BadRequest(new { message = "Cannot modify answers after submission" });

            var success = await _attemptService.ClearAnswerAsync(attemptId, questionId);
            return success ? Ok(new { message = "Answer cleared successfully" }) : BadRequest(new { message = "Failed to clear answer" });
        }

        #endregion

        #region 4. Navigation

        [HttpPost("{attemptId}/questions/{questionId}/next")]
        [Authorize]
        public async Task<IActionResult> NextQuestion(string attemptId, string questionId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var nextQuestionId = await _attemptService.NavigateQuestionAsync(attemptId, questionId, +1);
            return Ok(new { message = "Moved to next question", nextQuestionId });
        }

        [HttpPost("{attemptId}/questions/{questionId}/previous")]
        [Authorize]
        public async Task<IActionResult> PreviousQuestion(string attemptId, string questionId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var previousQuestionId = await _attemptService.NavigateQuestionAsync(attemptId, questionId, -1);
            return Ok(new { message = "Moved to previous question", previousQuestionId });
        }

        [HttpPost("{attemptId}/questions/{questionId}/mark-review")]
        [Authorize]
        public async Task<IActionResult> MarkForReview(string attemptId, string questionId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId()) return Forbid();

            var success = await _attemptService.ToggleFlagAsync(attemptId, questionId, true);
            return success ? Ok(new { message = "Question marked for review" }) : BadRequest(new { message = "Failed to mark for review" });
        }

        [HttpPost("{attemptId}/questions/{questionId}/unmark-review")]
        [Authorize]
        public async Task<IActionResult> UnmarkForReview(string attemptId, string questionId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId()) return Forbid();

            var success = await _attemptService.ToggleFlagAsync(attemptId, questionId, false);
            return success ? Ok(new { message = "Question review mark removed" }) : BadRequest(new { message = "Failed to unmark for review" });
        }

        [HttpGet("{attemptId}/navigation")]
        [Authorize]
        public async Task<IActionResult> GetNavigationState(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var nav = await _attemptService.GetNavigationStateAsync(attemptId);
            return nav != null ? Ok(nav) : NotFound(new { message = "Attempt not found" });
        }

        #endregion

        #region 5. Timer

        [HttpGet("{attemptId}/timer")]
        [Authorize]
        public async Task<IActionResult> GetTimer(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            var remaining = await _attemptService.CalculateTimeRemainingSecondsAsync(attempt);
            return Ok(new
            {
                attemptId,
                timeRemainingSeconds = remaining,
                extraTimeGrantedMinutes = attempt.ExtraTimeGrantedMinutes,
                status = attempt.Status.ToString(),
                isExpired = remaining <= 0 && attempt.Status == AttemptStatus.InProgress
            });
        }

        [HttpPost("{attemptId}/timer/sync")]
        [Authorize]
        public async Task<IActionResult> SyncTimer(string attemptId, [FromBody] TimerSyncRequest request)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId()) return Forbid();

            await _attemptService.SyncTimerAsync(attemptId, request.ClientRemainingSeconds, request.CurrentQuestionId);
            var serverRemaining = await _attemptService.CalculateTimeRemainingSecondsAsync(attempt);
            return Ok(new { message = "Timer synchronized", timeRemainingSeconds = serverRemaining });
        }

        [HttpPost("{attemptId}/extend-time")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ExtendTime(string attemptId, [FromBody] ExtendTimeRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _attemptService.ExtendTimeAsync(attemptId, request.ExtraMinutes, request.Reason);
            return success ? Ok(new { message = $"Added {request.ExtraMinutes} minutes" }) : NotFound(new { message = "Attempt not found" });
        }

        #endregion

        #region 6. Candidate Attempts

        [HttpGet("~/api/v1/users/{userId}/attempts")]
        [Authorize]
        public async Task<IActionResult> GetUserAttempts(string userId)
        {
            if (!IsStaff() && GetUserId() != userId) return Forbid();

            var attempts = await _attemptService.GetStudentAttemptsAsync(userId);
            return Ok(attempts.Select(AttemptManagementService.ToAttemptDto).ToList());
        }

        [HttpGet("~/api/v1/exams/{examId}/attempts")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetExamAttempts(string examId)
        {
            var attempts = await _attemptService.GetExamAttemptsAsync(examId);
            return Ok(attempts.Select(AttemptManagementService.ToAttemptDto).ToList());
        }

        [HttpGet("{attemptId}/events")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAttemptEvents(string attemptId)
        {
            var events = await _attemptService.GetAttemptEventsAsync(attemptId);
            if (events == null) return NotFound(new { message = "Attempt not found" });
            return Ok(events);
        }

        [HttpPost("{attemptId}/log")]
        [Authorize]
        public async Task<IActionResult> LogActivity(string attemptId, [FromBody] ActivityLogRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId() && !IsStaff()) return Forbid();

            var success = await _attemptService.LogActivityAsync(attemptId, request.Activity, request.EventType, request.QuestionId);
            return success ? Ok(new { message = "Activity logged" }) : BadRequest(new { message = "Failed to log activity" });
        }

        #endregion

        #region 7. Recovery

        [HttpPost("{attemptId}/recover")]
        [Authorize]
        public async Task<IActionResult> RecoverAttempt(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (!IsStaff() && attempt.StudentId != GetUserId()) return Forbid();

            if (attempt.Status == AttemptStatus.Paused)
                await _attemptService.ResumeAttemptAsync(attemptId);

            await _attemptService.LogActivityEventAsync(attemptId, EventType.Reconnected, "Session recovered");
            var remaining = await _attemptService.CalculateTimeRemainingSecondsAsync(attempt);
            return Ok(new { message = "Session recovered", attempt, timeRemainingSeconds = remaining });
        }

        [HttpPost("{attemptId}/resume-session")]
        [Authorize]
        public async Task<IActionResult> ResumeSession(string attemptId)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId() && !IsStaff()) return Forbid();

            if (attempt.Status == AttemptStatus.Paused)
                await _attemptService.ResumeAttemptAsync(attemptId);

            var remaining = await _attemptService.CalculateTimeRemainingSecondsAsync(attempt);
            return Ok(new { message = "Session resumed", attempt, timeRemainingSeconds = remaining });
        }

        [HttpPost("{attemptId}/sync")]
        [Authorize]
        public async Task<IActionResult> SyncAttempt(string attemptId, [FromBody] SyncAttemptRequest? request = null)
        {
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != GetUserId()) return Forbid();
            if (attempt.Status != AttemptStatus.InProgress)
                return BadRequest(new { message = "Attempt is not in progress" });

            var items = request?.Answers ?? new List<SyncAnswerItem>();
            await _attemptService.SyncAnswersBulkAsync(attemptId, items, request?.CurrentQuestionId);
            var remaining = await _attemptService.CalculateTimeRemainingSecondsAsync(attempt);
            return Ok(new { message = "Attempt state synchronized", syncedCount = items.Count, timeRemainingSeconds = remaining });
        }

        #endregion

        #region 8. Admin Interventions

        [HttpPost("{attemptId}/invalidate")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> InvalidateAttempt(string attemptId, [FromQuery] string? reason = null)
        {
            var success = await _attemptService.InvalidateAttemptAsync(attemptId, reason ?? "Invalidated by administrator");
            return success ? Ok(new { message = "Attempt invalidated" }) : NotFound(new { message = "Attempt not found" });
        }

        [HttpPost("{attemptId}/restore")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreAttempt(string attemptId)
        {
            var success = await _attemptService.RestoreAttemptAsync(attemptId);
            return success ? Ok(new { message = "Attempt restored" }) : NotFound(new { message = "Attempt not found" });
        }

        [HttpPost("{attemptId}/reopen")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ReopenAttempt(string attemptId)
        {
            var success = await _attemptService.ReopenAttemptAsync(attemptId);
            return success ? Ok(new { message = "Attempt reopened for modifications" }) : NotFound(new { message = "Attempt not found" });
        }

        [HttpPost("{attemptId}/grant-extra-time")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GrantExtraTime(string attemptId, [FromBody] ExtendTimeRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _attemptService.ExtendTimeAsync(attemptId, request.ExtraMinutes, request.Reason);
            return success ? Ok(new { message = $"Granted {request.ExtraMinutes} extra minutes" }) : NotFound(new { message = "Attempt not found" });
        }

        #endregion

        [HttpGet("~/api/v1/attempts/health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ExamAttemptService", version = "v1", timestamp = DateTime.UtcNow });
        }
    }
}
