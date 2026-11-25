using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResultService.Models;
using ResultService.Services;

namespace ResultService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/queries")]
    [Authorize]
    public class ResultQueriesController : ControllerBase
    {
        private readonly EvaluationService _evalService;

        public ResultQueriesController(EvaluationService evalService)
        {
            _evalService = evalService;
        }

        private string? GetUserId() => User.FindFirst("userId")?.Value;
        private bool IsStaff() => User.FindFirst(ClaimTypes.Role)?.Value is "Teacher" or "Admin" or "SuperAdmin";

        [HttpGet("users/{userId}/results")]
        public async Task<IActionResult> GetUserResults(string userId)
        {
            if (!IsStaff() && GetUserId() != userId) return Forbid();
            var results = await _evalService.GetResultsByUserAsync(userId);
            if (!IsStaff())
                results = results.Where(r => r.Status is ResultStatus.Published or ResultStatus.Finalized or ResultStatus.Calculated).ToList();
            return Ok(results);
        }

        [HttpGet("users/{userId}/certificates")]
        public async Task<IActionResult> GetUserCertificates(string userId)
        {
            if (!IsStaff() && GetUserId() != userId) return Forbid();
            return Ok(await _evalService.GetCertificatesByUserAsync(userId));
        }

        [HttpGet("exams/{examId}/results")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> GetExamResults(string examId) =>
            Ok(await _evalService.GetResultsByExamAsync(examId));

        [HttpGet("exams/{examId}/ranking")]
        public async Task<IActionResult> GetExamRanking(string examId)
        {
            var ranks = await _evalService.GetExamLeaderboardAsync(examId, 50);
            return Ok(new { ranks });
        }

        [HttpGet("exams/{examId}/leaderboard")]
        public async Task<IActionResult> GetExamLeaderboard(string examId)
        {
            var leaderboard = await _evalService.GetExamLeaderboardAsync(examId, 20);
            return Ok(new { leaderboard });
        }

        [HttpGet("attempts/{attemptId}/result")]
        public async Task<IActionResult> GetAttemptResult(string attemptId)
        {
            var result = await _evalService.GetResultByAttemptAsync(attemptId);
            if (result == null) return NotFound(new { message = "Result not found for this attempt" });
            if (!IsStaff() && result.UserId != GetUserId()) return Forbid();
            return Ok(result);
        }
    }
}
