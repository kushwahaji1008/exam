using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AuthService.Models;
using AuthService.Services;

namespace AuthService.V2.Controllers
{
    [ApiVersion("2.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/audit-security")]
    [Authorize(Roles = "Admin,SuperAdmin")] // Restrict to admin roles for audit compliance
    public class AuditSecurityController : ControllerBase
    {
        private readonly AuditService _auditService;
        private readonly ILogger<AuditSecurityController> _logger;

        public AuditSecurityController(AuditService auditService, ILogger<AuditSecurityController> logger)
        {
            _auditService = auditService;
            _logger = logger;
        }

        // ==========================================
        // 1. AUDIT LOGS
        // ==========================================

        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetAllAuditLogs([FromQuery] AuditFilterRequest filter)
        {
            // TODO: Implement pagination, date range filtering, and action type filtering
            var logs = await _auditService.GetAllAuditLogsAsync(filter);
            return Ok(logs);
        }

        [HttpGet("audit-logs/{auditId}")]
        public async Task<IActionResult> GetAuditLogById(string auditId)
        {
            var log = await _auditService.GetAuditLogByIdAsync(auditId);
            if (log == null) return NotFound(new { message = "Audit log not found" });

            return Ok(log);
        }

        [HttpGet("users/{userId}/audit-logs")]
        public async Task<IActionResult> GetUserAuditLogs(string userId, [FromQuery] AuditFilterRequest filter)
        {
            // Retrieves all actions performed BY the user, or actions performed ON the user
            var logs = await _auditService.GetUserAuditLogsAsync(userId, filter);
            if (logs == null) return NotFound(new { message = "User not found or no logs available" });

            return Ok(logs);
        }

        // ==========================================
        // 2. SECURITY EVENTS
        // ==========================================

        [HttpGet("security-events")]
        public async Task<IActionResult> GetAllSecurityEvents([FromQuery] SecurityEventFilterRequest filter)
        {
            // TODO: Implement filtering for failed logins, password changes, MFA failures, etc.
            var events = await _auditService.GetAllSecurityEventsAsync(filter);
            return Ok(events);
        }

        [HttpGet("security-events/{eventId}")]
        public async Task<IActionResult> GetSecurityEventById(string eventId)
        {
            var securityEvent = await _auditService.GetSecurityEventByIdAsync(eventId);
            if (securityEvent == null) return NotFound(new { message = "Security event not found" });

            return Ok(securityEvent);
        }
    }
}