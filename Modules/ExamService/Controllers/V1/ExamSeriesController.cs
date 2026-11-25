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

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateSeries([FromBody] CreateExamSeriesRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = User.FindFirst("userId")?.Value ?? "SYSTEM";

            var series = await _seriesService.CreateSeriesAsync(request, userId);
            return Created($"/api/v1/exam-series/{series.SeriesId}", new { message = "Series created successfully", data = series });
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllSeries()
        {
            var series = await _seriesService.GetAllSeriesAsync();
            return Ok(series);
        }

        [HttpGet("{seriesId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSeries(string seriesId)
        {
            var series = await _seriesService.GetSeriesByIdAsync(seriesId);
            if (series == null) return NotFound(new { message = "Exam series not found." });
            return Ok(series);
        }

        [HttpPut("{seriesId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateSeries(string seriesId, [FromBody] UpdateExamSeriesRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var updated = await _seriesService.UpdateSeriesAsync(seriesId, request);
            return updated ? Ok(new { message = "Exam series updated." }) : NotFound(new { message = "Exam series not found." });
        }

        [HttpDelete("{seriesId}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteSeries(string seriesId)
        {
            var deleted = await _seriesService.DeleteSeriesAsync(seriesId);
            return deleted ? Ok(new { message = "Exam series deleted." }) : NotFound(new { message = "Exam series not found." });
        }

        [HttpPost("{seriesId}/add-exam")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> AddExam(string seriesId, [FromBody] AddExamToSeriesRequest request)
        {
            var result = await _seriesService.AddExamToSeriesAsync(seriesId, request.ExamId);
            if (!result.Success) return BadRequest(new { message = result.Message });
            return Ok(new { message = result.Message });
        }

        [HttpDelete("{seriesId}/exams/{examId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> RemoveExam(string seriesId, string examId)
        {
            var result = await _seriesService.RemoveExamFromSeriesAsync(seriesId, examId);
            if (!result.Success) return NotFound(new { message = result.Message });
            return Ok(new { message = result.Message });
        }
    }
}
