using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics/exams/{examId}")]
    [Authorize(Roles = "Admin,Teacher")]
    public class ExamAnalyticsController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetExamOverview(string examId) => Ok(new { examId, totalAttempts = 450 });

        [HttpGet("performance")]
        public IActionResult GetPerformance(string examId) => Ok(new { averageScore = 72.4, highestScore = 98 });

        [HttpGet("completion")]
        public IActionResult GetCompletionStats(string examId) => Ok(new { completionRate = "94%" });

        [HttpGet("dropout")]
        public IActionResult GetDropoutStats(string examId) => Ok(new { dropoutRate = "6%" });

        [HttpGet("timing")]
        public IActionResult GetTimingStats(string examId) => Ok(new { averageCompletionTime = "45 mins" });

        [HttpGet("questions")]
        public IActionResult GetQuestionStats(string examId) => Ok(new { mostFailedQuestionId = "Q12" });

        [HttpGet("difficulty")]
        public IActionResult GetDifficultyIndex(string examId) => Ok(new { perceivedDifficulty = "Hard", score = 0.85 });

        [HttpGet("distribution")]
        public IActionResult GetScoreDistribution(string examId) => Ok(new { bellCurveData = new[] { 10, 20, 40, 20, 10 } });
    }
}