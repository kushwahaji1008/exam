using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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

        // --- User Queries ---
        [HttpGet("users/{userId}/results")]
        public async Task<IActionResult> GetUserResults(string userId) => 
            Ok(await _evalService.GetResultsByUserAsync(userId));

        [HttpGet("users/{userId}/certificates")]
        public async Task<IActionResult> GetUserCertificates(string userId) => 
            Ok(await _evalService.GetCertificatesByUserAsync(userId));

        // --- Exam Queries ---
        [HttpGet("exams/{examId}/results")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetExamResults(string examId) => 
            Ok(await _evalService.GetResultsByExamAsync(examId));

        [HttpGet("exams/{examId}/ranking")]
        public IActionResult GetExamRanking(string examId) => Ok(new { ranks = new List<object>() });

        [HttpGet("exams/{examId}/leaderboard")]
        public IActionResult GetExamLeaderboard(string examId) => Ok(new { leaderboard = new List<object>() });

        // --- Attempt Queries ---
        [HttpGet("attempts/{attemptId}/result")]
        public async Task<IActionResult> GetAttemptResult(string attemptId) => 
            Ok(await _evalService.GetResultByAttemptAsync(attemptId));
    }
}