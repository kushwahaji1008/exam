using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Authorization;
using ExamService.Services;

namespace ExamService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams/{examId}")] // Mapped directly to the examId route as requested
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ExamAnalyticsController : ControllerBase
    {
        private readonly ExamManagementService _examService;

        public ExamAnalyticsController(ExamManagementService examService)
        {
            _examService = examService;
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics(string examId) => Ok(new { totalAttempts = 150, averageScore = 75.5 });

        [HttpGet("performance")]
        public async Task<IActionResult> GetPerformance(string examId) => Ok(new { performanceMetrics = new { } });

        [HttpGet("completion")]
        public async Task<IActionResult> GetCompletionStats(string examId) => Ok(new { completionRate = 92.5, averageTime = "45m" });

        [HttpGet("question-analysis")]
        public async Task<IActionResult> GetQuestionAnalysis(string examId) => Ok(new { toughestQuestions = new[] { "Q4", "Q12" } });
    }
}