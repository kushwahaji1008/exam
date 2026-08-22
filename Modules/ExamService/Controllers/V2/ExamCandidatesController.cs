using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ExamService.Services;

namespace ExamService.V2.Controllers
{
    [ApiVersion("2.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams/{examId}/candidates")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ExamCandidatesController : ControllerBase
    {
        private readonly ExamManagementService _examService;

        public ExamCandidatesController(ExamManagementService examService)
        {
            _examService = examService;
        }

        [HttpGet]
        public async Task<IActionResult> GetCandidates(string examId) => Ok(new { candidates = new[] { "User1", "User2" } });

        [HttpPost]
        public async Task<IActionResult> AddCandidate(string examId, [FromBody] object candidate) => Ok(new { message = "Candidate added" });

        [HttpDelete("{userId}")]
        public async Task<IActionResult> RemoveCandidate(string examId, string userId) => Ok(new { message = "Candidate removed" });

        [HttpPost("bulk")]
        public async Task<IActionResult> AddCandidatesBulk(string examId, [FromBody] object request) => Ok(new { message = "Candidates added in bulk" });

        [HttpPost("{userId}/allow")]
        public async Task<IActionResult> AllowCandidate(string examId, string userId) => Ok(new { message = "Candidate allowed (unblocked)" });

        [HttpPost("{userId}/block")]
        public async Task<IActionResult> BlockCandidate(string examId, string userId) => Ok(new { message = "Candidate blocked" });
    }
}