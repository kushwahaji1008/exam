using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CourseService.Services;
using CourseService.Models.V1;

namespace CourseService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/courses/{courseId}/instructors")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class CourseInstructorsController : ControllerBase
    {
        private readonly CourseManagementService _courseService;

        public CourseInstructorsController(CourseManagementService courseService)
        {
            _courseService = courseService;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetInstructors(string courseId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            return course != null ? Ok(new { instructorIds = course.InstructorIds }) : NotFound();
        }

        [HttpPost]
        public async Task<IActionResult> AddInstructor(string courseId, [FromBody] AssignInstructorRequest request)
        {
            var success = await _courseService.AddInstructorAsync(courseId, request.InstructorId);
            return success ? Ok(new { message = "Instructor assigned successfully" }) : BadRequest();
        }

        [HttpDelete("{instructorId}")]
        public async Task<IActionResult> RemoveInstructor(string courseId, string instructorId)
        {
            var success = await _courseService.RemoveInstructorAsync(courseId, instructorId);
            return success ? Ok(new { message = "Instructor removed successfully" }) : NotFound();
        }
    }
}