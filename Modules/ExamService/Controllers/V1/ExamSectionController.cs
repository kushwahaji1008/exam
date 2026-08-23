using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Authorization;
using ExamService.Services;

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
        public async Task<IActionResult> GetSections(string examId) => Ok(new { sections = new[] { "SecA", "SecB" } });

        [HttpPost]
        public async Task<IActionResult> AddSection(string examId, [FromBody] object section) => Ok(new { message = "Section added", sectionId = "S1" });

        [HttpGet("{sectionId}")]
        public async Task<IActionResult> GetSectionById(string examId, string sectionId) => Ok(new { id = sectionId, name = "Section A" });

        [HttpPut("{sectionId}")]
        public async Task<IActionResult> UpdateSection(string examId, string sectionId, [FromBody] object section) => Ok(new { message = "Section updated" });

        [HttpDelete("{sectionId}")]
        public async Task<IActionResult> DeleteSection(string examId, string sectionId) => Ok(new { message = "Section deleted" });

        [HttpPost("reorder")]
        public async Task<IActionResult> ReorderSections(string examId, [FromBody] object orderRequest) => Ok(new { message = "Sections reordered" });
    }
}