using System.Security.Claims;
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
        private readonly ICourseEnrollmentLookup? _enrollmentLookup;

        public CourseCurriculumController(
            CourseManagementService courseService,
            ICourseEnrollmentLookup? enrollmentLookup = null)
        {
            _courseService = courseService;
            _enrollmentLookup = enrollmentLookup;
        }

        [HttpGet]
        [AllowAnonymous] 
        public async Task<IActionResult> GetItems(string courseId, string sectionId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            var section = course?.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (course == null || section == null) return NotFound();

            var userId = User.FindFirst("userId")?.Value;
            bool hasAccess = false;
            if (!string.IsNullOrEmpty(userId))
            {
                bool isStaff = User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin") ||
                               course.CreatedBy == userId ||
                               course.InstructorIds.Contains(userId);
                hasAccess = isStaff ||
                            course.CoursePrice == 0 ||
                            (_enrollmentLookup != null && await _enrollmentLookup.IsUserEnrolledAsync(userId, courseId));
            }

            var items = section.Items.OrderBy(i => i.OrderIndex).Select(i => new CurriculumItem
            {
                Id = i.Id,
                Title = i.Title,
                Type = i.Type,
                ContentUrl = (hasAccess || (!string.IsNullOrEmpty(userId) && i.IsFreePreview)) ? i.ContentUrl : string.Empty,
                OrderIndex = i.OrderIndex,
                IsFreePreview = i.IsFreePreview,
                DurationSeconds = i.DurationSeconds
            });

            return Ok(items);
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
            var success = await _courseService.PatchCurriculumItemAsync(courseId, sectionId, itemId, request);
            return success ? Ok(new { message = "Curriculum item updated" }) : NotFound(new { message = "Course, section, or item not found, or invalid payload" });
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