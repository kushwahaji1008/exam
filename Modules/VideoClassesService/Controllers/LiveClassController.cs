using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using VideoClassesService.Models;
using VideoClassesService.Services;

namespace VideoClassesService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/videos/live")]
    public class LiveClassController : ControllerBase
    {
        private readonly LiveClassService _liveClassService;

        public LiveClassController(LiveClassService liveClassService)
        {
            _liveClassService = liveClassService;
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateLiveClass([FromBody] LiveClass liveClass)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            liveClass.InstructorId = userId;
            liveClass.InstructorName = userName;

            var created = await _liveClassService.CreateLiveClassAsync(liveClass);
            return Ok(new { message = "Live class created", liveClass = created });
        }

        [HttpGet("{liveClassId}")]
        [Authorize]
        public async Task<IActionResult> GetLiveClass(string liveClassId)
        {
            var liveClass = await _liveClassService.GetLiveClassAsync(liveClassId);
            if (liveClass == null)
            {
                return NotFound(new { message = "Live class not found" });
            }

            return Ok(liveClass);
        }

        [HttpGet("upcoming")]
        [Authorize]
        public async Task<IActionResult> GetUpcoming()
        {
            var liveClasses = await _liveClassService.GetUpcomingLiveClassesAsync();
            return Ok(liveClasses);
        }

        [HttpGet("active")]
        [Authorize]
        public async Task<IActionResult> GetActive()
        {
            var liveClasses = await _liveClassService.GetActiveLiveClassesAsync();
            return Ok(liveClasses);
        }

        [HttpPost("{liveClassId}/start")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> StartLiveClass(string liveClassId)
        {
            var success = await _liveClassService.StartLiveClassAsync(liveClassId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to start live class" });
            }

            return Ok(new { message = "Live class started" });
        }

        [HttpPost("{liveClassId}/end")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> EndLiveClass(string liveClassId, [FromBody] EndLiveClassRequest? request)
        {
            var success = await _liveClassService.EndLiveClassAsync(liveClassId, request?.RecordingUrl);
            if (!success)
            {
                return BadRequest(new { message = "Failed to end live class" });
            }

            return Ok(new { message = "Live class ended" });
        }

        [HttpPost("{liveClassId}/join")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> JoinLiveClass(string liveClassId)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _liveClassService.JoinLiveClassAsync(liveClassId, userId);
            
            if (!success)
            {
                return BadRequest(new { message = "Failed to join live class (may be full)" });
            }

            return Ok(new { message = "Joined live class" });
        }
    }

    public class EndLiveClassRequest
    {
        public string? RecordingUrl { get; set; }
    }
}