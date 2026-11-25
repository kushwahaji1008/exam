using System.Text.Json;
using ExamService.Models;
using ExamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamService.Controllers.V1
{
    [ApiVersion("1.0")]
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
        public async Task<IActionResult> GetCandidates(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null) return NotFound(new { message = "Exam not found" });

            return Ok(new
            {
                candidates = exam.AllowedStudents,
                blocked = exam.BlockedStudents
            });
        }

        [HttpPost]
        public async Task<IActionResult> AddCandidate(string examId, [FromBody] JsonElement candidate)
        {
            string? userId = null;
            if (candidate.ValueKind == JsonValueKind.Object)
            {
                if (candidate.TryGetProperty("userId", out var u1) && u1.ValueKind == JsonValueKind.String) userId = u1.GetString();
                else if (candidate.TryGetProperty("UserId", out var u2) && u2.ValueKind == JsonValueKind.String) userId = u2.GetString();
            }

            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest(new { message = "userId is required." });

            var success = await _examService.AllowCandidateAsync(examId, userId);
            return success ? Ok(new { message = "Candidate added", userId }) : NotFound(new { message = "Exam not found" });
        }

        [HttpDelete("{userId}")]
        public async Task<IActionResult> RemoveCandidate(string examId, string userId)
        {
            var success = await _examService.RemoveCandidateAsync(examId, userId);
            return success ? Ok(new { message = "Candidate removed" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("bulk")]
        public async Task<IActionResult> AddCandidatesBulk(string examId, [FromBody] BulkCandidatesRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _examService.AddCandidatesBulkAsync(examId, request.UserIds);
            return success ? Ok(new { message = "Candidates added in bulk", count = request.UserIds.Count }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{userId}/allow")]
        public async Task<IActionResult> AllowCandidate(string examId, string userId)
        {
            var success = await _examService.AllowCandidateAsync(examId, userId);
            return success ? Ok(new { message = "Candidate allowed" }) : NotFound(new { message = "Exam not found" });
        }

        [HttpPost("{userId}/block")]
        public async Task<IActionResult> BlockCandidate(string examId, string userId)
        {
            var success = await _examService.BlockCandidateAsync(examId, userId);
            return success ? Ok(new { message = "Candidate blocked" }) : NotFound(new { message = "Exam not found" });
        }
    }
}
