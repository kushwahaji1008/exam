using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace ProctoringService.Hubs
{
    [Authorize]
    public class ProctoringHub : Hub
    {
        private readonly ILogger<ProctoringHub> _logger;

        public ProctoringHub(ILogger<ProctoringHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            _logger.LogInformation("User {UserId} connected to proctoring hub", userId);

            // Add to appropriate groups
            if (role == "Student")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"student_{userId}");
            }
            else if (role == "Teacher" || role == "Admin" || role == "SuperAdmin")
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

        // Student sends violation alerts
        public async Task ReportViolation(string sessionId, string violationType, string description)
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            
            // Notify proctors
            await Clients.Group("proctors").SendAsync("ViolationReported", new
            {
                sessionId,
                studentId = userId,
                violationType,
                description,
                timestamp = DateTime.UtcNow
            });
        }

        // Student sends live frame for analysis
        public async Task SendFrame(string sessionId, string frameBase64)
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            
            // Broadcast to monitoring proctors
            await Clients.Group("proctors").SendAsync("LiveFrame", new
            {
                sessionId,
                studentId = userId,
                frame = frameBase64,
                timestamp = DateTime.UtcNow
            });
        }

        // Proctor joins monitoring for specific student
        public async Task MonitorStudent(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"monitoring_{sessionId}");
            _logger.LogInformation("Proctor monitoring session {SessionId}", sessionId);
        }

        // Proctor stops monitoring
        public async Task StopMonitoring(string sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"monitoring_{sessionId}");
        }

        // Send alert to specific student
        public async Task SendAlertToStudent(string studentId, string message)
        {
            await Clients.Group($"student_{studentId}").SendAsync("ProctorAlert", message);
        }

        // Suspend student exam
        public async Task SuspendStudent(string sessionId, string studentId, string reason)
        {
            await Clients.Group($"student_{studentId}").SendAsync("ExamSuspended", new
            {
                sessionId,
                reason,
                timestamp = DateTime.UtcNow
            });
        }
    }
}