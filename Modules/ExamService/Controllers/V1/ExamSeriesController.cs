using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExamService.Models;
using ExamService.Services;

namespace ExamService.Controllers.V1
{
    [ApiController]
    [Route("api/v1/exam-series")]
    public class ExamSeriesController : ControllerBase
    {
        private readonly ExamSeriesManagementService _seriesService;

        public ExamSeriesController(ExamSeriesManagementService seriesService)
        {
            _seriesService = seriesService;
        }

        // 1. Create a new Exam Series (Admin/Creator only)
        [HttpPost]
        [Authorize(Roles = "1,2,Teacher,Admin")] // Assuming only logged in admins/teachers can create
        public async Task<IActionResult> CreateSeries([FromBody] CreateExamSeriesRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? "SYSTEM";
            
            var series = await _seriesService.CreateSeriesAsync(request, userId);
            return Created($"/api/v1/exam-series/{series.SeriesId}", new { message = "Series created successfully", data = series });
        }

        // 2. Get all published series (Public for students/marketing page)
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllSeries()
        {
            var series = await _seriesService.GetAllSeriesAsync();
            return Ok(series);
        }

        // 3. Get specific series details
        [HttpGet("{seriesId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSeries(string seriesId)
        {
            var series = await _seriesService.GetSeriesByIdAsync(seriesId);
            if (series == null) return NotFound(new { message = "Exam series not found." });
            
            return Ok(series);
        }

        // 4. Add an Exam to the Series
        [HttpPost("{seriesId}/add-exam")]
        [Authorize]
        public async Task<IActionResult> AddExam(string seriesId, [FromBody] AddExamToSeriesRequest request)
        {
            var result = await _seriesService.AddExamToSeriesAsync(seriesId, request.ExamId);
            
            if (!result.Success) return BadRequest(new { message = result.Message });
            
            return Ok(new { message = result.Message });
        }
    }
}