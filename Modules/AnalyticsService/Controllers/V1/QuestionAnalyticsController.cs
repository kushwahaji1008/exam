using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics/questions/{questionId}")]
    [Authorize(Roles = "Admin,Teacher")]
    public class QuestionAnalyticsController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetQuestionOverview(string questionId) => Ok(new { questionId, totalAppearances = 1200 });

        [HttpGet("accuracy")]
        public IActionResult GetAccuracy(string questionId) => Ok(new { correctRate = "45%", incorrectRate = "55%" });

        [HttpGet("difficulty")]
        public IActionResult GetDifficulty(string questionId) => Ok(new { difficultyLevel = "Hard" });

        [HttpGet("responses")]
        public IActionResult GetResponseDistribution(string questionId) => Ok(new { optionA = "20%", optionB = "50%", optionC = "15%", optionD = "15%" });

        [HttpGet("discrimination")]
        public IActionResult GetDiscriminationIndex(string questionId) => Ok(new { index = 0.65, meaning = "Good differentiator" });
    }
}