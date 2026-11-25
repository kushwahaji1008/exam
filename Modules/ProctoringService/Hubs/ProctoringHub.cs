using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ProctoringService.Services;

namespace ProctoringService.Hubs
{
    [Authorize]
    public class ProctoringHub : Hub
    {
        private readonly ILogger<ProctoringHub> _logger;
        private readonly ProctoringManagementService _proctoringService;

        public ProctoringHub(ILogger<ProctoringHub> logger, ProctoringManagementService proctoringService)
        {
            _logger = logger;
            _proctoringService = proctoringService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            _logger.LogInformation("User {UserId} connected to proctoring hub", userId);

            if (role == "Student")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"student_{userId}");
            }
            else if (role is "Teacher" or "Admin" or "SuperAdmin")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "proctors");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            _logger.LogInformation("User {UserId} disconnected from proctoring hub", userId);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task ReportViolation(string sessionId, string violationType, string description)
        {
            var userId = Context.User?.FindFirst("userId")?.Value;

            await Clients.Group("proctors").SendAsync("ViolationReported", new
            {
                sessionId,
                studentId = userId,
                violationType,
                description,
                timestamp = DateTime.UtcNow
            });
        }

        public async Task SendFrame(string sessionId, string frameBase64)
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            var payload = new
            {
                sessionId,
                studentId = userId,
                frame = frameBase64,
                timestamp = DateTime.UtcNow
            };

            await Clients.Group("proctors").SendAsync("LiveFrame", payload);
            await Clients.Group($"monitoring_{sessionId}").SendAsync("LiveFrame", payload);
        }

        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task MonitorStudent(string sessionId)
        {
            var proctorId = Context.User?.FindFirst("userId")?.Value;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"monitoring_{sessionId}");
            await _proctoringService.SetMonitoringStateAsync(sessionId, true, proctorId);
            _logger.LogInformation("Proctor {ProctorId} monitoring session {SessionId}", proctorId, sessionId);
        }

        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task StopMonitoring(string sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"monitoring_{sessionId}");
            await _proctoringService.SetMonitoringStateAsync(sessionId, false, null);
        }

        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task SendAlertToStudent(string studentId, string message)
        {
            await Clients.Group($"student_{studentId}").SendAsync("ProctorAlert", message);
        }

        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task SuspendStudent(string sessionId, string studentId, string reason)
        {
            await _proctoringService.SuspendSessionAsync(sessionId, reason);
            await Clients.Group($"student_{studentId}").SendAsync("ExamSuspended", new
            {
                sessionId,
                reason,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
