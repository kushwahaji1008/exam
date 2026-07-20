using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ProctoringService.Models;
using ProctoringService.Services;

namespace ProctoringService.Controllers
{
    [ApiController]
    [Route("api/proctoring")]
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

        [HttpPost("start")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> StartProctoring([FromBody] StartProctoringRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            
            var session = await _proctoringService.StartSessionAsync(request.AttemptId, request.ExamId, userId);
            
            return Ok(new { message = "Proctoring session started", session });
        }

        [HttpGet("session/{sessionId}")]
        [Authorize]
        public async Task<IActionResult> GetSession(string sessionId)
        {
            var session = await _proctoringService.GetSessionAsync(sessionId);
            
            if (session == null)
            {
                return NotFound(new { message = "Session not found" });
            }

            // Students can only view their own sessions
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            
            if (role == "Student" && session.StudentId != userId)
            {
                return Forbid();
            }

            return Ok(session);
        }

        [HttpGet("attempt/{attemptId}")]
        [Authorize]
        public async Task<IActionResult> GetSessionByAttempt(string attemptId)
        {
            var session = await _proctoringService.GetActiveSessionByAttemptAsync(attemptId);
            
            if (session == null)
            {
                return NotFound(new { message = "No active proctoring session found" });
            }

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
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> ReportViolation([FromBody] ReportViolationRequest request)
        {
            var violation = new Violation
            {
                Type = request.Type,
                Description = request.Description,
                Severity = request.Severity,
                Timestamp = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(request.SnapshotBase64))
            {
                // Store snapshot reference
                violation.SnapshotUrl = Guid.NewGuid().ToString();
            }

            var success = await _proctoringService.ReportViolationAsync(request.SessionId, violation);
            
            if (!success)
            {
                return BadRequest(new { message = "Failed to report violation" });
            }

            return Ok(new { message = "Violation reported", violation });
        }

        [HttpPost("snapshot")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> SubmitSnapshot([FromBody] SubmitSnapshotRequest request)
        {
            var snapshot = new Snapshot
            {
                ImageBase64 = request.ImageBase64,
                Type = request.Type,
                Timestamp = DateTime.UtcNow
            };

            var success = await _proctoringService.SubmitSnapshotAsync(request.SessionId, snapshot);
            
            if (!success)
            {
                return BadRequest(new { message = "Failed to submit snapshot" });
            }

            return Ok(new { message = "Snapshot submitted", snapshotId = snapshot.Id, analysis = snapshot.Analysis });
        }

        [HttpPost("session/{sessionId}/end")]
        [Authorize]
        public async Task<IActionResult> EndSession(string sessionId)
        {
            var session = await _proctoringService.GetSessionAsync(sessionId);
            
            if (session == null)
            {
                return NotFound(new { message = "Session not found" });
            }

            // Students can only end their own sessions
            var userId = User.FindFirst("userId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            
            if (role == "Student" && session.StudentId != userId)
            {
                return Forbid();
            }

            var success = await _proctoringService.EndSessionAsync(sessionId);
            
            if (!success)
            {
                return BadRequest(new { message = "Failed to end session" });
            }

            return Ok(new { message = "Proctoring session ended" });
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "ProctoringService", timestamp = DateTime.UtcNow });
        }
    }
}