using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ExamAttemptService.Models;
using ExamAttemptService.Services;
using System.Security.Claims;

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

        #region 1. Attempts (CRUD)

        [HttpGet]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAllAttempts()
        {
            // TODO: Implement GetAllAttemptsAsync in _attemptService
            return Ok(new { message = "List of all attempts" });
        }

        [HttpPost]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> CreateAttempt([FromBody] StartExamRequest request)
        {
            // Maps to your existing Start logic, creating the attempt record
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

            var attempt = await _attemptService.StartExamAsync(request.ExamId, userId, userName);
            return Ok(new { message = "Attempt created", attempt });
        }

        [HttpGet("{attemptId}")]
        [Authorize]
        public async Task<IActionResult> GetAttempt(string attemptId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);
            if (attempt == null) return NotFound(new { message = "Attempt not found" });

            if (role == "Student" && attempt.StudentId != userId) return Forbid();

            return Ok(attempt);
        }

        [HttpDelete("{attemptId}")]
        [Authorize(Roles = "1,2,3,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteAttempt(string attemptId)
        {
            // TODO: Implement DeleteAttemptAsync in _attemptService
            return Ok(new { message = $"Attempt {attemptId} deleted successfully" });
        }

        #endregion

        #region 2. Start/End Lifecycle

        [HttpPost("{attemptId}/start")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> StartExam(string attemptId)
        {
            // TODO: Implement logic to formally begin the timer/session for an existing attempt
            return Ok(new { message = "Exam session started" });
        }

        [HttpPost("{attemptId}/pause")]
        [Authorize(Roles = "0,Student,Teacher,Admin")]
        public async Task<IActionResult> PauseAttempt(string attemptId)
        {
            // TODO: Implement PauseAttemptAsync
            return Ok(new { message = "Exam paused" });
        }

        [HttpPost("{attemptId}/resume")]
        [Authorize(Roles = "0,Student,Teacher,Admin")]
        public async Task<IActionResult> ResumeAttempt(string attemptId)
        {
            // TODO: Implement ResumeAttemptAsync
            return Ok(new { message = "Exam resumed" });
        }

        [HttpPost("{attemptId}/submit")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> SubmitExam(string attemptId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != userId) return Forbid();
            if (attempt.Status != AttemptStatus.InProgress) 
                return BadRequest(new { message = "Exam already submitted or not in progress" });

            var success = await _attemptService.SubmitExamAsync(attemptId);
            if (!success) return BadRequest(new { message = "Failed to submit exam" });

            return Ok(new { message = "Exam submitted successfully" });
        }

        [HttpPost("{attemptId}/force-submit")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ForceSubmitExam(string attemptId)
        {
            // TODO: Implement ForceSubmit logic bypassing student checks
            var success = await _attemptService.SubmitExamAsync(attemptId);
            return Ok(new { message = "Exam force-submitted" });
        }

        [HttpPost("{attemptId}/terminate")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> TerminateExam(string attemptId)
        {
            // TODO: Implement Terminate logic (e.g., due to cheating/violation)
            return Ok(new { message = "Exam terminated" });
        }

        #endregion

        #region 3. Questions & Answers

        [HttpGet("{attemptId}/questions")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> GetQuestions(string attemptId)
        {
            // TODO: Retrieve all questions for this attempt
            return Ok(new { message = "Questions fetched" });
        }

        [HttpGet("{attemptId}/questions/{questionId}")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> GetQuestion(string attemptId, string questionId)
        {
            // TODO: Retrieve a specific question
            return Ok(new { message = $"Question {questionId} fetched" });
        }

        [HttpPost("{attemptId}/questions/{questionId}/answer")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> SaveAnswer(string attemptId, string questionId, [FromBody] SubmitAnswerRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != userId) return Forbid();
            if (attempt.Status != AttemptStatus.InProgress) 
                return BadRequest(new { message = "Cannot modify answers after submission" });

            var answer = new Answer
            {
                QuestionId = questionId, // overridden from path variable
                SelectedOption = request.SelectedOption,
                SelectedOptions = request.SelectedOptions,
                TextAnswer = request.TextAnswer,
                CodeAnswer = request.CodeAnswer
            };

            var success = await _attemptService.SaveAnswerAsync(attemptId, answer);
            if (!success) return BadRequest(new { message = "Failed to save answer" });

            return Ok(new { message = "Answer saved successfully" });
        }

        [HttpPut("{attemptId}/questions/{questionId}/answer")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> UpdateAnswer(string attemptId, string questionId, [FromBody] SubmitAnswerRequest request)
        {
            // Map to the exact same logic as POST or implement separate Update logic in service
            return await SaveAnswer(attemptId, questionId, request);
        }

        [HttpDelete("{attemptId}/questions/{questionId}/answer")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> ClearAnswer(string attemptId, string questionId)
        {
            // TODO: Implement ClearAnswerAsync in _attemptService
            return Ok(new { message = "Answer cleared successfully" });
        }

        #endregion

        #region 4. Navigation

        [HttpPost("{attemptId}/questions/{questionId}/next")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> NextQuestion(string attemptId, string questionId)
        {
            // TODO: Log navigation or return next question ID
            return Ok(new { message = "Moved to next question" });
        }

        [HttpPost("{attemptId}/questions/{questionId}/previous")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> PreviousQuestion(string attemptId, string questionId)
        {
            // TODO: Log navigation or return previous question ID
            return Ok(new { message = "Moved to previous question" });
        }

        [HttpPost("{attemptId}/questions/{questionId}/mark-review")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> MarkForReview(string attemptId, string questionId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != userId) return Forbid();

            var success = await _attemptService.ToggleFlagAsync(attemptId, questionId, true);
            if (!success) return BadRequest(new { message = "Failed to mark for review" });

            return Ok(new { message = "Question marked for review" });
        }

        [HttpPost("{attemptId}/questions/{questionId}/unmark-review")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> UnmarkForReview(string attemptId, string questionId)
        {
            var userId = User.FindFirst("userId")?.Value;
            var attempt = await _attemptService.GetAttemptByIdAsync(attemptId);

            if (attempt == null) return NotFound(new { message = "Attempt not found" });
            if (attempt.StudentId != userId) return Forbid();

            var success = await _attemptService.ToggleFlagAsync(attemptId, questionId, false);
            if (!success) return BadRequest(new { message = "Failed to unmark for review" });

            return Ok(new { message = "Question review mark removed" });
        }

        [HttpGet("{attemptId}/navigation")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> GetNavigationState(string attemptId)
        {
            // TODO: Return a map of questions showing answered/flagged/unvisited states
            return Ok(new { message = "Navigation state fetched" });
        }

        #endregion

        #region 5. Timer

        [HttpGet("{attemptId}/timer")]
        [Authorize]
        public async Task<IActionResult> GetTimer(string attemptId)
        {
            // TODO: Return time remaining based on start time and allotted time
            return Ok(new { timeRemainingSeconds = 3600 });
        }

        [HttpPost("{attemptId}/timer/sync")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> SyncTimer(string attemptId, [FromBody] TimerSyncRequest request)
        {
            // TODO: Implement timer sync logic (e.g., handling client disconnects)
            return Ok(new { message = "Timer synchronized" });
        }

        [HttpPost("{attemptId}/extend-time")]
        [Authorize(Roles = "1,2,3,Teacher,Admin")]
        public async Task<IActionResult> ExtendTime(string attemptId, [FromBody] ExtendTimeRequest request)
        {
            // TODO: Implement time extension logic in _attemptService
            return Ok(new { message = $"Added {request.ExtraMinutes} minutes" });
        }

        #endregion

        #region 6. Candidate Attempts

        // Uses "~/" to override the controller's route prefix
        [HttpGet("~/api/v1/users/{userId}/attempts")]
        [Authorize]
        public async Task<IActionResult> GetUserAttempts(string userId)
        {
            var requestingUserId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (role == "Student" && requestingUserId != userId) return Forbid();

            var attempts = await _attemptService.GetStudentAttemptsAsync(userId);
            var attemptDtos = attempts.Select(AttemptManagementService.ToAttemptDto).ToList();
            return Ok(attemptDtos);
        }

        // Uses "~/" to override the controller's route prefix
        [HttpGet("~/api/v1/exams/{examId}/attempts")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetExamAttempts(string examId)
        {
            var attempts = await _attemptService.GetExamAttemptsAsync(examId);
            var attemptDtos = attempts.Select(AttemptManagementService.ToAttemptDto).ToList();
            return Ok(attemptDtos);
        }

        [HttpGet("{attemptId}/events")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetAttemptEvents(string attemptId)
        {
            // TODO: Fetch activity logs/events for the attempt
            return Ok(new { message = "Events fetched" });
        }

        // Keeping your old /log endpoint logic slightly adjusted just in case you need POST
        [HttpPost("{attemptId}/log")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> LogActivity(string attemptId, [FromBody] ActivityLogRequest request)
        {
            var success = await _attemptService.LogActivityAsync(attemptId, request.Activity);
            if (!success) return BadRequest(new { message = "Failed to log activity" });
            return Ok(new { message = "Activity logged" });
        }

        #endregion

        #region 7. Recovery

        [HttpPost("{attemptId}/recover")]
        [Authorize(Roles = "0,Student,Admin")]
        public async Task<IActionResult> RecoverAttempt(string attemptId)
        {
            // TODO: Implement logic to recover a crashed browser session
            return Ok(new { message = "Session recovered" });
        }

        [HttpPost("{attemptId}/resume-session")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> ResumeSession(string attemptId)
        {
            // TODO: Handle returning to an active session
            return Ok(new { message = "Session resumed" });
        }

        [HttpPost("{attemptId}/sync")]
        [Authorize(Roles = "0,Student")]
        public async Task<IActionResult> SyncAttempt(string attemptId)
        {
            // TODO: Implement bulk sync of local-storage answers to server
            return Ok(new { message = "Attempt state synchronized" });
        }

        #endregion

        #region 8. Admin Interventions

        [HttpPost("{attemptId}/invalidate")]
        [Authorize(Roles = "1,2,3,Admin,SuperAdmin")]
        public async Task<IActionResult> InvalidateAttempt(string attemptId)
        {
            // TODO: Implement invalidation logic (e.g., mark as void due to malpractice)
            return Ok(new { message = "Attempt invalidated" });
        }

        [HttpPost("{attemptId}/restore")]
        [Authorize(Roles = "1,2,3,Admin,SuperAdmin")]
        public async Task<IActionResult> RestoreAttempt(string attemptId)
        {
            // TODO: Restore a previously deleted/invalidated attempt
            return Ok(new { message = "Attempt restored" });
        }

        [HttpPost("{attemptId}/reopen")]
        [Authorize(Roles = "1,2,3,Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> ReopenAttempt(string attemptId)
        {
            // TODO: Reopen a submitted exam for a student
            return Ok(new { message = "Attempt reopened for modifications" });
        }

        [HttpPost("{attemptId}/grant-extra-time")]
        [Authorize(Roles = "1,2,3,Teacher,Admin")]
        public async Task<IActionResult> GrantExtraTime(string attemptId, [FromBody] ExtendTimeRequest request)
        {
            // TODO: Add extra minutes to a specific attempt
            return Ok(new { message = $"Granted {request.ExtraMinutes} extra minutes" });
        }

        #endregion

        [HttpGet("~/api/v1/attempts/health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ExamAttemptService", version = "v1", timestamp = DateTime.UtcNow });
        }
    }

    // Example additional DTOs needed for the new endpoints
    public class TimerSyncRequest
    {
        public int ClientRemainingSeconds { get; set; }
    }

    public class ExtendTimeRequest
    {
        public int ExtraMinutes { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class ActivityLogRequest
    {
        public string Activity { get; set; } = string.Empty;
    }
}