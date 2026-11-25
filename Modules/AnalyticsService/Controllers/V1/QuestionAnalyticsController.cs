using AnalyticsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics/questions/{questionId}")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public class QuestionAnalyticsController : ControllerBase
    {
        private readonly AnalyticsManagementService _analytics;

        public QuestionAnalyticsController(AnalyticsManagementService analytics)
        {
            _analytics = analytics;
        }

        [HttpGet]
        public async Task<IActionResult> GetQuestionOverview(string questionId) =>
            Ok(await _analytics.GetQuestionAnalyticsAsync(questionId));

        [HttpGet("accuracy")]
        public async Task<IActionResult> GetAccuracy(string questionId) =>
            Ok(await _analytics.GetQuestionAnalyticsAsync(questionId));

        [HttpGet("difficulty")]
        public async Task<IActionResult> GetDifficulty(string questionId) =>
            Ok(await _analytics.GetQuestionAnalyticsAsync(questionId));

        [HttpGet("responses")]
        public async Task<IActionResult> GetResponseDistribution(string questionId) =>
            Ok(await _analytics.GetQuestionAnalyticsAsync(questionId));

        [HttpGet("discrimination")]
        public async Task<IActionResult> GetDiscriminationIndex(string questionId) =>
            Ok(await _analytics.GetQuestionAnalyticsAsync(questionId));
    }
}
