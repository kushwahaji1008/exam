using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ResultService.Models;
using ResultService.Services;
using System.Text;

namespace ResultService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/results")]
    [Authorize]
    public class ResultsController : ControllerBase
    {
        private readonly EvaluationService _evalService;

        public ResultsController(EvaluationService evalService)
        {
            _evalService = evalService;
        }
        private string? GetUserId() => User.FindFirst("userId")?.Value;

        // ==========================================
        // 1. CORE & LIFECYCLE
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetAllResults() => Ok(await _evalService.GetAllResultsAsync());

        [HttpGet("{resultId}")]
        public async Task<IActionResult> GetResult(string resultId) => Ok(await _evalService.GetResultByIdAsync(resultId));

        [HttpPost("{resultId}/calculate")]
        [HttpPost("{resultId}/recalculate")]
        [Authorize(Roles = "Admin,Teacher,System")]
        public async Task<IActionResult> CalculateResult(string resultId) => 
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Calculated) });

        [HttpPost("{resultId}/finalize")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> FinalizeResult(string resultId) => 
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Finalized) });

        [HttpPost("{resultId}/unfinalize")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> UnfinalizeResult(string resultId) => 
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Calculated) });

        [HttpPost("{resultId}/publish")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> PublishResult(string resultId) => 
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Published) });

        [HttpPost("{resultId}/unpublish")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> UnpublishResult(string resultId) => 
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Finalized) }); // Reverts to finalized

        [HttpPost("bulk-publish")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> BulkPublish([FromBody] BulkPublishRequest request) => 
            Ok(new { success = await _evalService.BulkPublishAsync(request.ResultIds) });

        // ==========================================
        // 2. MANUAL GRADING
        // ==========================================
        [HttpGet("{resultId}/manual-grading")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetManualGradingNeeds(string resultId)
        {
            var result = await _evalService.GetResultByIdAsync(resultId);
            if (result == null) return NotFound();
            return Ok(result.Breakdown.Where(q => q.NeedsManualGrading));
        }

        [HttpPost("{resultId}/grade")]
        [Authorize(Roles = "Admin,Teacher")]
        public IActionResult GradeEntireExam(string resultId) => Ok(new { message = "Full manual review submitted" });

        [HttpPut("{resultId}/questions/{questionId}/grade")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GradeQuestion(string resultId, string questionId, [FromBody] GradeQuestionRequest req) => 
            Ok(new { success = await _evalService.GradeQuestionAsync(resultId, questionId, req.Score, GetUserId()!) });

        [HttpPost("{resultId}/questions/{questionId}/override")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> OverrideQuestionGrade(string resultId, string questionId, [FromBody] OverrideGradeRequest req) => 
            Ok(new { success = await _evalService.GradeQuestionAsync(resultId, questionId, req.NewScore, GetUserId()!, req.Reason) });

        // ==========================================
        // 3. REPORTS & EXPORTS
        // ==========================================
        [HttpGet("{resultId}/report")]
        public IActionResult GetFullReport(string resultId) => Ok(new { reportUrl = $"/reports/{resultId}.pdf" });

        [HttpGet("{resultId}/breakdown")]
        public IActionResult GetBreakdown(string resultId) => Ok(new { topics = new { Math = 85, Science = 90 } });

        [HttpGet("{resultId}/rank")]
        public IActionResult GetStudentRank(string resultId) => Ok(new { rank = 12, totalStudents = 150, percentile = 92.0 });

        [HttpPost("export")]
        [HttpPost("export/csv")]
        [Authorize(Roles = "Admin,Teacher")]
        public IActionResult ExportCsv([FromBody] ExportFilterRequest req)
        {
            byte[] fileBytes = Encoding.UTF8.GetBytes("Id,Score,Status\n1,95,Published");
            return File(fileBytes, "text/csv", "results.csv");
        }

        [HttpPost("export/excel")]
        [Authorize(Roles = "Admin,Teacher")]
        public IActionResult ExportExcel([FromBody] ExportFilterRequest req) => Ok(new { downloadUrl = "/exports/data.xlsx" });

        [HttpPost("export/pdf")]
        [Authorize(Roles = "Admin,Teacher")]
        public IActionResult ExportPdf([FromBody] ExportFilterRequest req) => Ok(new { downloadUrl = "/exports/data.pdf" });
    }
}