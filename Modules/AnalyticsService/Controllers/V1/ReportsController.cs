using AnalyticsService.Models;
using AnalyticsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics/reports")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public class ReportsController : ControllerBase
    {
        private readonly AnalyticsManagementService _analyticsService;

        public ReportsController(AnalyticsManagementService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        private string? GetUserId() => User.FindFirst("userId")?.Value;

        [HttpPost]
        public async Task<IActionResult> CreateReport([FromBody] CreateReportRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            return Ok(await _analyticsService.CreateReportAsync(req, GetUserId() ?? "SYSTEM"));
        }

        [HttpGet]
        public async Task<IActionResult> GetAllReports() => Ok(await _analyticsService.GetAllReportsAsync());

        [HttpGet("{reportId}")]
        public async Task<IActionResult> GetReport(string reportId)
        {
            var report = await _analyticsService.GetReportByIdAsync(reportId);
            return report != null ? Ok(report) : NotFound(new { message = "Report not found" });
        }

        [HttpPost("{reportId}/generate")]
        public async Task<IActionResult> GenerateReport(string reportId)
        {
            var success = await _analyticsService.ProcessReportAsync(reportId);
            return success ? Ok(new { success = true }) : NotFound(new { message = "Report not found" });
        }

        [HttpGet("{reportId}/download")]
        public async Task<IActionResult> DownloadReport(string reportId)
        {
            var report = await _analyticsService.GetReportByIdAsync(reportId);
            if (report == null) return NotFound(new { message = "Report not found" });
            if (report.Status != ReportStatus.Completed) return BadRequest(new { message = "Report not ready" });

            var fileBytes = await _analyticsService.BuildReportPdfBytesAsync(report);
            return File(fileBytes, "application/pdf", $"Report_{reportId}.pdf");
        }

        [HttpDelete("{reportId}")]
        public async Task<IActionResult> DeleteReport(string reportId)
        {
            var deleted = await _analyticsService.DeleteReportAsync(reportId);
            return deleted ? Ok(new { success = true }) : NotFound(new { message = "Report not found" });
        }
    }
}
