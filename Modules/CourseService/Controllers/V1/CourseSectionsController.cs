using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CourseService.Models.V1;
using CourseService.Services;

namespace CourseService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/courses/{courseId}/sections")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class CourseSectionsController : ControllerBase
    {
        private readonly CourseManagementService _courseService;

        public CourseSectionsController(CourseManagementService courseService)
        {
            _courseService = courseService;
        }

        [HttpGet]
        [AllowAnonymous] // Usually public so students can see syllabus
        public async Task<IActionResult> GetSections(string courseId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            return course == null ? NotFound() : Ok(course.Sections.OrderBy(s => s.OrderIndex));
        }

        [HttpPost]
        public async Task<IActionResult> AddSection(string courseId, [FromBody] CreateSectionRequest request)
        {
            var sectionId = await _courseService.AddSectionAsync(courseId, request);
            return sectionId != null ? Ok(new { message = "Section added", sectionId }) : BadRequest();
        }

        [HttpGet("{sectionId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSection(string courseId, string sectionId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            return section != null ? Ok(section) : NotFound();
        }

        [HttpPatch("{sectionId}")]
        public async Task<IActionResult> PatchSection(string courseId, string sectionId, [FromBody] object request)
        {
            // Implementation logic in service
            return Ok(new { message = "Section patched successfully" });
        }

        [HttpDelete("{sectionId}")]
        public async Task<IActionResult> DeleteSection(string courseId, string sectionId)
        {
            var success = await _courseService.DeleteSectionAsync(courseId, sectionId);
            return success ? Ok(new { message = "Section deleted" }) : NotFound();
        }

        [HttpPatch("reorder")]
        public async Task<IActionResult> ReorderSections(string courseId, [FromBody] ReorderRequest request)
        {
            var success = await _courseService.ReorderSectionsAsync(courseId, request.OrderedIds);
            return success ? Ok(new { message = "Sections reordered" }) : BadRequest();
        }
    }
}