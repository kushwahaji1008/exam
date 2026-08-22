using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CourseService.Models.V1;
using CourseService.Services;

namespace CourseService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/courses/{courseId}/sections/{sectionId}/items")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public class CourseCurriculumController : ControllerBase
    {
        private readonly CourseManagementService _courseService;

        public CourseCurriculumController(CourseManagementService courseService)
        {
            _courseService = courseService;
        }

        [HttpGet]
        [AllowAnonymous] 
        public async Task<IActionResult> GetItems(string courseId, string sectionId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            return section != null ? Ok(section.Items.OrderBy(i => i.OrderIndex)) : NotFound();
        }

        [HttpPost]
        public async Task<IActionResult> AddItem(string courseId, string sectionId, [FromBody] CreateCurriculumItemRequest request)
        {
            var itemId = await _courseService.AddCurriculumItemAsync(courseId, sectionId, request);
            return itemId != null ? Ok(new { message = "Item added", itemId }) : BadRequest();
        }

        [HttpPatch("{itemId}")]
        public async Task<IActionResult> PatchItem(string courseId, string sectionId, string itemId, [FromBody] object request)
        {
            return Ok(new { message = "Curriculum item updated" });
        }

        [HttpDelete("{itemId}")]
        public async Task<IActionResult> DeleteItem(string courseId, string sectionId, string itemId)
        {
            var success = await _courseService.DeleteCurriculumItemAsync(courseId, sectionId, itemId);
            return success ? Ok(new { message = "Item deleted" }) : NotFound();
        }

        [HttpPatch("reorder")]
        public async Task<IActionResult> ReorderItems(string courseId, string sectionId, [FromBody] ReorderRequest request)
        {
            var success = await _courseService.ReorderCurriculumItemsAsync(courseId, sectionId, request.OrderedIds);
            return success ? Ok(new { message = "Items reordered" }) : BadRequest();
        }
    }
}