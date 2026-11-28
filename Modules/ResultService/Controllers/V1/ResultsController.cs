using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResultService.Models;
using ResultService.Services;

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
        private bool IsStaff() => User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        // ==========================================
        // 1. CORE & LIFECYCLE
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetAllResults()
        {
            if (IsStaff())
                return Ok(await _evalService.GetAllResultsAsync());

            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var ownResults = await _evalService.GetResultsByUserAsync(userId);
            return Ok(ownResults.Where(r => r.Status is ResultStatus.Published or ResultStatus.Finalized or ResultStatus.Calculated).ToList());
        }

        [HttpGet("{resultId}")]
        public async Task<IActionResult> GetResult(string resultId)
        {
            var result = await _evalService.GetResultByIdAsync(resultId);
            if (result == null) return NotFound(new { message = "Result not found" });
            if (!IsStaff() && result.UserId != GetUserId()) return Forbid();

            return Ok(result);
        }

        [HttpPost("evaluate/{attemptId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin,System")]
        public async Task<IActionResult> EvaluateAttempt(string attemptId, [FromQuery] bool recalculate = false)
        {
            var evaluated = await _evalService.EvaluateAttemptAsync(attemptId, recalculate);
            return evaluated != null ? Ok(evaluated) : NotFound(new { message = "Attempt could not be evaluated or was not found." });
        }

        [HttpPost("{resultId}/calculate")]
        [HttpPost("{resultId}/recalculate")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher,System")]
        public async Task<IActionResult> CalculateResult(string resultId) =>
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Calculated) });

        [HttpPost("{resultId}/finalize")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> FinalizeResult(string resultId) =>
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Finalized) });

        [HttpPost("{resultId}/unfinalize")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> UnfinalizeResult(string resultId) =>
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Calculated) });

        [HttpPost("{resultId}/publish")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> PublishResult(string resultId) =>
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Published) });

        [HttpPost("{resultId}/unpublish")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> UnpublishResult(string resultId) =>
            Ok(new { success = await _evalService.ChangeStatusAsync(resultId, ResultStatus.Finalized) });

        [HttpPost("bulk-publish")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> BulkPublish([FromBody] BulkPublishRequest request) =>
            Ok(new { success = await _evalService.BulkPublishAsync(request.ResultIds) });

        // ==========================================
        // 2. MANUAL GRADING
        // ==========================================
        [HttpGet("{resultId}/manual-grading")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> GetManualGradingNeeds(string resultId)
        {
            var result = await _evalService.GetResultByIdAsync(resultId);
            if (result == null) return NotFound();
            return Ok(result.Breakdown.Where(q => q.NeedsManualGrading));
        }

        [HttpPost("{resultId}/grade")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> GradeEntireExam(string resultId)
        {
            var success = await _evalService.FinalizeManualReviewAsync(resultId);
            return success ? Ok(new { message = "Full manual review submitted" }) : NotFound(new { message = "Result not found" });
        }

        [HttpPut("{resultId}/questions/{questionId}/grade")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> GradeQuestion(string resultId, string questionId, [FromBody] GradeQuestionRequest req) =>
            Ok(new { success = await _evalService.GradeQuestionAsync(resultId, questionId, req.Score, GetUserId()!) });

        [HttpPost("{resultId}/questions/{questionId}/override")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> OverrideQuestionGrade(string resultId, string questionId, [FromBody] OverrideGradeRequest req) =>
            Ok(new { success = await _evalService.GradeQuestionAsync(resultId, questionId, req.NewScore, GetUserId()!, req.Reason) });

        // ==========================================
        // 3. REPORTS & EXPORTS
        // ==========================================
        [HttpGet("{resultId}/report")]
        public async Task<IActionResult> GetFullReport(string resultId)
        {
            var result = await _evalService.GetResultByIdAsync(resultId);
            if (result == null) return NotFound(new { message = "Result not found" });
            if (!IsStaff() && result.UserId != GetUserId()) return Forbid();

            var pdfBytes = _evalService.GenerateResultReportPdf(result);
            return File(pdfBytes, "application/pdf", $"ResultReport_{resultId}.pdf");
        }

        [HttpGet("{resultId}/breakdown")]
        public async Task<IActionResult> GetBreakdown(string resultId)
        {
            var result = await _evalService.GetResultByIdAsync(resultId);
            if (result == null) return NotFound(new { message = "Result not found" });
            if (!IsStaff() && result.UserId != GetUserId()) return Forbid();

            return Ok(new
            {
                resultId = result.Id,
                examId = result.ExamId,
                totalQuestions = result.Breakdown.Count,
                correctCount = result.Breakdown.Count(b => b.IsCorrect),
                incorrectCount = result.Breakdown.Count(b => !b.IsCorrect && !b.NeedsManualGrading),
                pendingManualCount = result.Breakdown.Count(b => b.NeedsManualGrading),
                totalScore = result.TotalScore,
                maxScore = result.MaxScore,
                percentage = Math.Round(result.Percentage, 2),
                questions = result.Breakdown
            });
        }

        [HttpGet("{resultId}/rank")]
        public async Task<IActionResult> GetStudentRank(string resultId)
        {
            var result = await _evalService.GetResultByIdAsync(resultId);
            if (result == null) return NotFound(new { message = "Result not found" });
            if (!IsStaff() && result.UserId != GetUserId()) return Forbid();

            var stats = await _evalService.GetStudentRankAsync(resultId);
            if (stats == null) return NotFound();

            return Ok(new
            {
                rank = stats.Value.Rank,
                totalStudents = stats.Value.TotalStudents,
                percentile = stats.Value.Percentile
            });
        }

        [HttpPost("export")]
        [HttpPost("export/csv")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> ExportCsv([FromBody] ExportFilterRequest req)
        {
            var fileBytes = await _evalService.ExportResultsCsvAsync(req);
            return File(fileBytes, "text/csv", $"results_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }

        [HttpPost("export/excel")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> ExportExcel([FromBody] ExportFilterRequest req)
        {
            var csvBytes = await _evalService.ExportResultsCsvAsync(req);
            // Prefix UTF-8 BOM for clean Microsoft Excel compatibility
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var payload = new byte[bom.Length + csvBytes.Length];
            Buffer.BlockCopy(bom, 0, payload, 0, bom.Length);
            Buffer.BlockCopy(csvBytes, 0, payload, bom.Length, csvBytes.Length);
            return File(payload, "text/csv", $"results_excel_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }

        [HttpPost("export/pdf")]
        [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
        public async Task<IActionResult> ExportPdf([FromBody] ExportFilterRequest req)
        {
            var pdfBytes = await _evalService.ExportResultsPdfAsync(req);
            return File(pdfBytes, "application/pdf", $"results_report_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
        }
    }
}
