using ExamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams/{examId}")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ExamAnalyticsController : ControllerBase
    {
        private readonly ExamManagementService _examService;

        public ExamAnalyticsController(ExamManagementService examService)
        {
            _examService = examService;
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics(string examId)
        {
            var stats = await _examService.GetExamStatisticsAsync(examId);
            return stats != null ? Ok(stats) : NotFound(new { message = "Exam not found" });
        }

        [HttpGet("performance")]
        public async Task<IActionResult> GetPerformance(string examId)
        {
            var metrics = await _examService.GetPerformanceMetricsAsync(examId);
            return metrics != null ? Ok(metrics) : NotFound(new { message = "Exam not found" });
        }

        [HttpGet("completion")]
        public async Task<IActionResult> GetCompletionStats(string examId)
        {
            var stats = await _examService.GetCompletionStatsAsync(examId);
            return stats != null ? Ok(stats) : NotFound(new { message = "Exam not found" });
        }

        [HttpGet("question-analysis")]
        public async Task<IActionResult> GetQuestionAnalysis(string examId)
        {
            var analysis = await _examService.GetQuestionAnalysisAsync(examId);
            return analysis != null ? Ok(analysis) : NotFound(new { message = "Exam not found" });
        }
    }
}
