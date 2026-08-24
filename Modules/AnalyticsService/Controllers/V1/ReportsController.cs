using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AnalyticsService.Models;
using AnalyticsService.Services;

namespace AnalyticsService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/analytics/reports")]
    [Authorize(Roles = "Admin,Teacher")]
    public class ReportsController : ControllerBase
    {
        private readonly AnalyticsManagementService _analyticsService;

        public ReportsController(AnalyticsManagementService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        private string? GetUserId() => User.FindFirst("userId")?.Value;

        [HttpPost]
        public async Task<IActionResult> CreateReport([FromBody] CreateReportRequest req) => 
            Ok(await _analyticsService.CreateReportAsync(req, GetUserId()!));

        [HttpGet]
        public async Task<IActionResult> GetAllReports() => Ok(await _analyticsService.GetAllReportsAsync());

        [HttpGet("{reportId}")]
        public async Task<IActionResult> GetReport(string reportId) => Ok(await _analyticsService.GetReportByIdAsync(reportId));

        [HttpPost("{reportId}/generate")]
        public async Task<IActionResult> GenerateReport(string reportId) => 
            Ok(new { success = await _analyticsService.ProcessReportAsync(reportId) });

        [HttpGet("{reportId}/download")]
        public async Task<IActionResult> DownloadReport(string reportId)
        {
            var report = await _analyticsService.GetReportByIdAsync(reportId);
            if (report == null || report.Status != ReportStatus.Completed) return BadRequest("Report not ready");
            
            byte[] fileBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // Mock PDF byte array
            return File(fileBytes, "application/pdf", $"Report_{reportId}.pdf");
        }

        [HttpDelete("{reportId}")]
        public async Task<IActionResult> DeleteReport(string reportId) => 
            Ok(new { success = await _analyticsService.DeleteReportAsync(reportId) });
    }
}