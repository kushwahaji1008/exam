using ExamService.Models;
using ExamService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/exams/{examId}/sections")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class ExamSectionsController : ControllerBase
    {
        private readonly ExamManagementService _examService;

        public ExamSectionsController(ExamManagementService examService)
        {
            _examService = examService;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetSections(string examId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null) return NotFound(new { message = "Exam not found" });

            return Ok(new { sections = exam.Sections.OrderBy(s => s.OrderIndex).ToList() });
        }

        [HttpPost]
        public async Task<IActionResult> AddSection(string examId, [FromBody] CreateSectionRequest section)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var sectionId = await _examService.AddSectionAsync(examId, section);
            if (sectionId == null) return NotFound(new { message = "Exam not found" });

            return Ok(new { message = "Section added", sectionId });
        }

        [HttpGet("{sectionId}")]
        [Authorize]
        public async Task<IActionResult> GetSectionById(string examId, string sectionId)
        {
            var exam = await _examService.GetExamByIdAsync(examId);
            if (exam == null) return NotFound(new { message = "Exam not found" });

            var section = exam.Sections.FirstOrDefault(s => s.Id == sectionId);
            return section != null ? Ok(section) : NotFound(new { message = "Section not found" });
        }

        [HttpPut("{sectionId}")]
        public async Task<IActionResult> UpdateSection(string examId, string sectionId, [FromBody] UpdateSectionRequest section)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _examService.UpdateSectionAsync(examId, sectionId, section);
            return success ? Ok(new { message = "Section updated" }) : NotFound(new { message = "Exam or section not found" });
        }

        [HttpDelete("{sectionId}")]
        public async Task<IActionResult> DeleteSection(string examId, string sectionId)
        {
            var success = await _examService.DeleteSectionAsync(examId, sectionId);
            return success ? Ok(new { message = "Section deleted" }) : NotFound(new { message = "Exam or section not found" });
        }

        [HttpPost("reorder")]
        public async Task<IActionResult> ReorderSections(string examId, [FromBody] ReorderRequest orderRequest)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var success = await _examService.ReorderSectionsAsync(examId, orderRequest.OrderedIds);
            return success ? Ok(new { message = "Sections reordered" }) : BadRequest(new { message = "Invalid section IDs or exam not found" });
        }
    }
}
