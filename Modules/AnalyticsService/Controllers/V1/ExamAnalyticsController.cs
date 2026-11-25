using AnalyticsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics/exams/{examId}")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public class ExamAnalyticsController : ControllerBase
    {
        private readonly AnalyticsManagementService _analytics;

        public ExamAnalyticsController(AnalyticsManagementService analytics)
        {
            _analytics = analytics;
        }

        [HttpGet]
        public async Task<IActionResult> GetExamOverview(string examId) =>
            Ok(await _analytics.GetExamOverviewAsync(examId));

        [HttpGet("performance")]
        public async Task<IActionResult> GetPerformance(string examId) =>
            Ok(await _analytics.GetExamPerformanceAsync(examId));

        [HttpGet("completion")]
        public async Task<IActionResult> GetCompletionStats(string examId) =>
            Ok(await _analytics.GetExamCompletionAsync(examId));

        [HttpGet("dropout")]
        public async Task<IActionResult> GetDropoutStats(string examId) =>
            Ok(await _analytics.GetExamDropoutAsync(examId));

        [HttpGet("timing")]
        public async Task<IActionResult> GetTimingStats(string examId) =>
            Ok(await _analytics.GetExamTimingAsync(examId));

        [HttpGet("questions")]
        public async Task<IActionResult> GetQuestionStats(string examId) =>
            Ok(await _analytics.GetExamQuestionStatsAsync(examId));

        [HttpGet("difficulty")]
        public async Task<IActionResult> GetDifficultyIndex(string examId) =>
            Ok(await _analytics.GetExamDifficultyAsync(examId));

        [HttpGet("distribution")]
        public async Task<IActionResult> GetScoreDistribution(string examId) =>
            Ok(await _analytics.GetExamScoreDistributionAsync(examId));
    }
}
