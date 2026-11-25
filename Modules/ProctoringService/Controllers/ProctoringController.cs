using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProctoringService.Models;
using ProctoringService.Services;

namespace ProctoringService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/proctoring")]
    public class ProctoringController : ControllerBase
    {
        private readonly ProctoringManagementService _proctoringService;
        private readonly ILogger<ProctoringController> _logger;

        public ProctoringController(
            ProctoringManagementService proctoringService,
            ILogger<ProctoringController> logger)
        {
            _proctoringService = proctoringService;
            _logger = logger;
        }

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private bool IsStaff() => User.FindFirst(ClaimTypes.Role)?.Value is "Teacher" or "Admin" or "SuperAdmin";

        [HttpPost("start")]
        [Authorize]
        public async Task<IActionResult> StartProctoring([FromBody] StartProctoringRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = GetUserId() ?? string.Empty;

            var session = await _proctoringService.StartSessionAsync(request.AttemptId, request.ExamId, userId);
            return Ok(new { message = "Proctoring session started", session });
        }

        [HttpGet("session/{sessionId}")]
        [Authorize]
        public async Task<IActionResult> GetSession(string sessionId)
        {
            var session = await _proctoringService.GetSessionAsync(sessionId);
            if (session == null) return NotFound(new { message = "Session not found" });
            if (!IsStaff() && session.StudentId != GetUserId()) return Forbid();

            return Ok(session);
        }

        [HttpGet("attempt/{attemptId}")]
        [Authorize]
        public async Task<IActionResult> GetSessionByAttempt(string attemptId)
        {
            var session = await _proctoringService.GetActiveSessionByAttemptAsync(attemptId);
            if (session == null) return NotFound(new { message = "No active proctoring session found" });
            if (!IsStaff() && session.StudentId != GetUserId()) return Forbid();

            return Ok(session);
        }

        [HttpGet("exam/{examId}/sessions")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetExamSessions(string examId)
        {
            var sessions = await _proctoringService.GetSessionsByExamAsync(examId);
            return Ok(sessions);
        }

        [HttpPost("violation")]
        [Authorize]
        public async Task<IActionResult> ReportViolation([FromBody] ReportViolationRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var session = await _proctoringService.GetSessionAsync(request.SessionId);
            if (session == null) return NotFound(new { message = "Session not found" });
            if (!IsStaff() && session.StudentId != GetUserId()) return Forbid();

            var violation = new Violation
            {
                Type = request.Type,
                Description = request.Description,
                Severity = request.Severity,
                Timestamp = DateTime.UtcNow,
                SnapshotUrl = !string.IsNullOrEmpty(request.SnapshotBase64) ? Guid.NewGuid().ToString() : null
            };

            var success = await _proctoringService.ReportViolationAsync(request.SessionId, violation);
            if (!success) return BadRequest(new { message = "Failed to report violation" });

            return Ok(new { message = "Violation reported", violation });
        }

        [HttpPost("snapshot")]
        [Authorize]
        public async Task<IActionResult> SubmitSnapshot([FromBody] SubmitSnapshotRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var session = await _proctoringService.GetSessionAsync(request.SessionId);
            if (session == null) return NotFound(new { message = "Session not found" });
            if (!IsStaff() && session.StudentId != GetUserId()) return Forbid();

            var snapshot = new Snapshot
            {
                ImageBase64 = request.ImageBase64,
                Type = request.Type,
                Timestamp = DateTime.UtcNow
            };

            var success = await _proctoringService.SubmitSnapshotAsync(request.SessionId, snapshot);
            if (!success) return BadRequest(new { message = "Failed to submit snapshot or snapshot exceeds size limit" });

            return Ok(new { message = "Snapshot submitted", snapshotId = snapshot.Id, analysis = snapshot.Analysis });
        }

        [HttpPost("session/{sessionId}/suspend")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> SuspendSession(string sessionId, [FromQuery] string? reason = null)
        {
            var success = await _proctoringService.SuspendSessionAsync(sessionId, reason ?? "Suspended by proctor");
            return success ? Ok(new { message = "Proctoring session suspended" }) : NotFound(new { message = "Session not found" });
        }

        [HttpPost("session/{sessionId}/end")]
        [Authorize]
        public async Task<IActionResult> EndSession(string sessionId)
        {
            var session = await _proctoringService.GetSessionAsync(sessionId);
            if (session == null) return NotFound(new { message = "Session not found" });
            if (!IsStaff() && session.StudentId != GetUserId()) return Forbid();

            var success = await _proctoringService.EndSessionAsync(sessionId);
            if (!success) return BadRequest(new { message = "Failed to end session" });

            return Ok(new { message = "Proctoring session ended" });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ProctoringService", timestamp = DateTime.UtcNow });
        }
    }
}
