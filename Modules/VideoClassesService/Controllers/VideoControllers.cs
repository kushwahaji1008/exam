using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using VideoClassesService.Models;
using VideoClassesService.Services;

namespace VideoClassesService.Controllers
{
    [ApiController]
    [Route("api/videos/courses")]
    public class CoursesController : ControllerBase
    {
        private readonly CourseManagementService _courseService;
        private readonly ProgressTrackingService _progressService;
        private readonly ILogger<CoursesController> _logger;

        public CoursesController(
            CourseManagementService courseService,
            ProgressTrackingService progressService,
            ILogger<CoursesController> logger)
        {
            _courseService = courseService;
            _progressService = progressService;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateCourse([FromBody] Course course)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            course.InstructorId = userId;
            course.InstructorName = userName;

            var created = await _courseService.CreateCourseAsync(course);
            return Ok(new { message = "Course created", course = created });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllCourses([FromQuery] CourseStatus? status, [FromQuery] CourseCategory? category)
        {
            var courses = await _courseService.GetAllCoursesAsync(status, category);
            return Ok(courses);
        }

        [HttpGet("{courseId}")]
        public async Task<IActionResult> GetCourse(string courseId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null)
            {
                return NotFound(new { message = "Course not found" });
            }

            return Ok(course);
        }

        [HttpPut("{courseId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateCourse(string courseId, [FromBody] Course course)
        {
            var success = await _courseService.UpdateCourseAsync(courseId, course);
            if (!success)
            {
                return BadRequest(new { message = "Failed to update course" });
            }

            return Ok(new { message = "Course updated" });
        }

        [HttpDelete("{courseId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteCourse(string courseId)
        {
            var success = await _courseService.DeleteCourseAsync(courseId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to delete course" });
            }

            return Ok(new { message = "Course deleted" });
        }

        [HttpPost("{courseId}/publish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishCourse(string courseId)
        {
            var success = await _courseService.PublishCourseAsync(courseId);
            if (!success)
            {
                return BadRequest(new { message = "Failed to publish course" });
            }

            return Ok(new { message = "Course published" });
        }

        [HttpPost("{courseId}/enroll")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> EnrollCourse(string courseId)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var progress = await _progressService.EnrollStudentAsync(userId, courseId);

            return Ok(new { message = "Enrolled successfully", progress });
        }

        [HttpGet("{courseId}/chapters")]
        public async Task<IActionResult> GetChapters(string courseId)
        {
            var chapters = await _courseService.GetChaptersByCourseAsync(courseId);
            return Ok(chapters);
        }

        [HttpPost("{courseId}/chapters")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateChapter(string courseId, [FromBody] Chapter chapter)
        {
            chapter.CourseId = courseId;
            var created = await _courseService.CreateChapterAsync(chapter);
            return Ok(new { message = "Chapter created", chapter = created });
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchCourses([FromQuery] string query)
        {
            var courses = await _courseService.SearchCoursesAsync(query);
            return Ok(courses);
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "VideoClassesService", timestamp = DateTime.UtcNow });
        }
    }

    [ApiController]
    [Route("api/videos/lessons")]
    public class LessonsController : ControllerBase
    {
        private readonly CourseManagementService _courseService;
        private readonly ProgressTrackingService _progressService;
        private readonly CommentService _commentService;

        public LessonsController(
            CourseManagementService courseService,
            ProgressTrackingService progressService,
            CommentService commentService)
        {
            _courseService = courseService;
            _progressService = progressService;
            _commentService = commentService;
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateLesson([FromBody] Lesson lesson)
        {
            var created = await _courseService.CreateLessonAsync(lesson);
            return Ok(new { message = "Lesson created", lesson = created });
        }

        [HttpGet("{lessonId}")]
        public async Task<IActionResult> GetLesson(string lessonId)
        {
            var lesson = await _courseService.GetLessonByIdAsync(lessonId);
            if (lesson == null)
            {
                return NotFound(new { message = "Lesson not found" });
            }

            // Increment view count
            await _courseService.IncrementViewCountAsync(lessonId);

            return Ok(lesson);
        }

        [HttpPost("{lessonId}/progress")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> UpdateProgress(string lessonId, [FromBody] ProgressUpdate update)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            
            var success = await _progressService.UpdateLessonProgressAsync(
                userId,
                update.CourseId,
                lessonId,
                update.WatchedSeconds,
                update.TotalSeconds
            );

            if (!success)
            {
                return BadRequest(new { message = "Failed to update progress" });
            }

            return Ok(new { message = "Progress updated" });
        }

        [HttpPost("{lessonId}/notes")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> AddNote(string lessonId, [FromBody] AddNoteRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            
            var note = new Note
            {
                Timestamp = request.Timestamp,
                Content = request.Content,
                CreatedAt = DateTime.UtcNow
            };

            var success = await _progressService.AddNoteAsync(userId, request.CourseId, lessonId, note);
            if (!success)
            {
                return BadRequest(new { message = "Failed to add note" });
            }

            return Ok(new { message = "Note added", note });
        }

        [HttpPost("{lessonId}/bookmarks")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> AddBookmark(string lessonId, [FromBody] AddBookmarkRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            
            var success = await _progressService.AddBookmarkAsync(userId, request.CourseId, lessonId, request.Timestamp);
            if (!success)
            {
                return BadRequest(new { message = "Failed to add bookmark" });
            }

            return Ok(new { message = "Bookmark added" });
        }

        [HttpGet("{lessonId}/comments")]
        public async Task<IActionResult> GetComments(string lessonId)
        {
            var comments = await _commentService.GetLessonCommentsAsync(lessonId);
            return Ok(comments);
        }

        [HttpPost("{lessonId}/comments")]
        [Authorize]
        public async Task<IActionResult> AddComment(string lessonId, [FromBody] AddCommentRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            var comment = new VideoComment
            {
                LessonId = lessonId,
                StudentId = userId,
                StudentName = userName,
                Content = request.Content,
                Timestamp = request.Timestamp,
                ParentCommentId = request.ParentCommentId
            };

            var created = await _commentService.AddCommentAsync(comment);
            return Ok(new { message = "Comment added", comment = created });
        }
    }

    [ApiController]
    [Route("api/videos/progress")]
    public class ProgressController : ControllerBase
    {
        private readonly ProgressTrackingService _progressService;

        public ProgressController(ProgressTrackingService progressService)
        {
            _progressService = progressService;
        }

        [HttpGet("my-courses")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyCourses()
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var progress = await _progressService.GetStudentCoursesAsync(userId);
            return Ok(progress);
        }

        [HttpGet("course/{courseId}")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetCourseProgress(string courseId)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var progress = await _progressService.GetStudentProgressAsync(userId, courseId);
            
            if (progress == null)
            {
                return NotFound(new { message = "No progress found" });
            }

            return Ok(progress);
        }

        [HttpPost("course/{courseId}/rate")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> RateCourse(string courseId, [FromBody] RateCourseRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            
            var success = await _progressService.RateCourseAsync(userId, courseId, request.Rating, request.Review);
            if (!success)
            {
                return BadRequest(new { message = "Failed to rate course" });
            }

            return Ok(new { message = "Course rated" });
        }
    }

    // Request DTOs
    public class ProgressUpdate
    {
        public string CourseId { get; set; } = string.Empty;
        public int WatchedSeconds { get; set; }
        public int TotalSeconds { get; set; }
    }

    public class AddNoteRequest
    {
        public string CourseId { get; set; } = string.Empty;
        public int Timestamp { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class AddBookmarkRequest
    {
        public string CourseId { get; set; } = string.Empty;
        public int Timestamp { get; set; }
    }

    public class AddCommentRequest
    {
        public string Content { get; set; } = string.Empty;
        public int? Timestamp { get; set; }
        public string? ParentCommentId { get; set; }
    }

    public class RateCourseRequest
    {
        public int Rating { get; set; }
        public string? Review { get; set; }
    }
}
