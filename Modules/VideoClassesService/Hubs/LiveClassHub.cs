using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using VideoClassesService.Models;
using VideoClassesService.Services;

namespace VideoClassesService.Hubs
{
    [Authorize]
    public class LiveClassHub : Hub
    {
        private readonly LiveClassService _liveClassService;
        private readonly ILogger<LiveClassHub> _logger;

        public LiveClassHub(LiveClassService liveClassService, ILogger<LiveClassHub> logger)
        {
            _liveClassService = liveClassService;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            _logger.LogInformation("User {UserId} connected to live class hub", userId);
            await base.OnConnectedAsync();
        }

        public async Task JoinClass(string liveClassId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"liveclass_{liveClassId}");
            var userId = Context.User?.FindFirst("userId")?.Value;
            var userName = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

            await Clients.Group($"liveclass_{liveClassId}").SendAsync("UserJoined", new
            {
                userId,
                userName,
                timestamp = DateTime.UtcNow
            });
        }

        public async Task LeaveClass(string liveClassId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"liveclass_{liveClassId}");
            var userId = Context.User?.FindFirst("userId")?.Value;
            var userName = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

            await Clients.Group($"liveclass_{liveClassId}").SendAsync("UserLeft", new
            {
                userId,
                userName,
                timestamp = DateTime.UtcNow
            });
        }

        public async Task SendMessage(string liveClassId, string message)
        {
            var userId = Context.User?.FindFirst("userId")?.Value ?? string.Empty;
            var userName = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            var chatMessage = new ChatMessage
            {
                SenderId = userId,
                SenderName = userName,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Type = MessageType.Text
            };

            await _liveClassService.AddChatMessageAsync(liveClassId, chatMessage);

            await Clients.Group($"liveclass_{liveClassId}").SendAsync("NewMessage", chatMessage);
        }

        public async Task RaiseHand(string liveClassId)
        {
            var userId = Context.User?.FindFirst("userId")?.Value ?? string.Empty;
            var userName = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            var chatMessage = new ChatMessage
            {
                SenderId = userId,
                SenderName = userName,
                Message = "Raised hand",
                Timestamp = DateTime.UtcNow,
                Type = MessageType.RaisedHand
            };

            await _liveClassService.AddChatMessageAsync(liveClassId, chatMessage);

            await Clients.Group($"liveclass_{liveClassId}").SendAsync("HandRaised", new
            {
                userId,
                userName,
                timestamp = DateTime.UtcNow
            });
        }

        public async Task CreatePoll(string liveClassId, Poll poll)
        {
            await _liveClassService.CreatePollAsync(liveClassId, poll);
            await Clients.Group($"liveclass_{liveClassId}").SendAsync("NewPoll", poll);
        }

        public async Task VotePoll(string liveClassId, string pollId, string optionId)
        {
            var userId = Context.User?.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _liveClassService.VotePollAsync(liveClassId, pollId, optionId, userId);

            if (success)
            {
                var liveClass = await _liveClassService.GetLiveClassAsync(liveClassId);
                var poll = liveClass?.Polls.FirstOrDefault(p => p.Id == pollId);
                
                await Clients.Group($"liveclass_{liveClassId}").SendAsync("PollUpdated", poll);
            }
        }

        public async Task ShareScreen(string liveClassId, bool isSharing)
        {
            var userId = Context.User?.FindFirst("userId")?.Value;
            var userName = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

            await Clients.Group($"liveclass_{liveClassId}").SendAsync("ScreenShareUpdate", new
            {
                userId,
                userName,
                isSharing,
                timestamp = DateTime.UtcNow
            });
        }
    }
}