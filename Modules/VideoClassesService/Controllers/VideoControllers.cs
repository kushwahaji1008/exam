using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VideoClassesService.Models;
using VideoClassesService.Services;

namespace VideoClassesService.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/videos/courses")]
    public class VideosController : ControllerBase
    {
        private readonly CourseManagementService _courseService;
        private readonly ProgressTrackingService _progressService;
        private readonly ILogger<VideosController> _logger;

        public VideosController(
            CourseManagementService courseService,
            ProgressTrackingService progressService,
            ILogger<VideosController> logger)
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
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

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
            if (course == null) return NotFound(new { message = "Course not found" });
            return Ok(course);
        }

        [HttpPut("{courseId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateCourse(string courseId, [FromBody] Course course)
        {
            var success = await _courseService.UpdateCourseAsync(courseId, course);
            if (!success) return NotFound(new { message = "Course not found" });
            return Ok(new { message = "Course updated" });
        }

        [HttpDelete("{courseId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteCourse(string courseId)
        {
            var success = await _courseService.DeleteCourseAsync(courseId);
            if (!success) return NotFound(new { message = "Course not found" });
            return Ok(new { message = "Course deleted" });
        }

        [HttpPost("{courseId}/publish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishCourse(string courseId)
        {
            var success = await _courseService.PublishCourseAsync(courseId);
            if (!success) return NotFound(new { message = "Course not found" });
            return Ok(new { message = "Course published" });
        }

        [HttpPost("{courseId}/enroll")]
        [Authorize]
        public async Task<IActionResult> EnrollCourse(string courseId)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            try
            {
                var progress = await _progressService.EnrollStudentAsync(userId, courseId);
                return Ok(new { message = "Enrolled successfully", progress });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
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

        [HttpPut("chapters/{chapterId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateChapter(string chapterId, [FromBody] Chapter chapter)
        {
            var success = await _courseService.UpdateChapterAsync(chapterId, chapter);
            return success ? Ok(new { message = "Chapter updated" }) : NotFound(new { message = "Chapter not found" });
        }

        [HttpDelete("chapters/{chapterId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteChapter(string chapterId)
        {
            var success = await _courseService.DeleteChapterAsync(chapterId);
            return success ? Ok(new { message = "Chapter deleted" }) : NotFound(new { message = "Chapter not found" });
        }

        [HttpGet("chapters/{chapterId}/lessons")]
        public async Task<IActionResult> GetLessonsByChapter(string chapterId)
        {
            var lessons = await _courseService.GetLessonsByChapterAsync(chapterId);
            return Ok(lessons);
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

    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/videos/lessons")]
    public class LessonsController : ControllerBase
    {
        private readonly CourseManagementService _courseService;
        private readonly ProgressTrackingService _progressService;
        private readonly CommentService _commentService;
        private readonly IVideoMediaSigner? _mediaSigner;

        public LessonsController(
            CourseManagementService courseService,
            ProgressTrackingService progressService,
            CommentService commentService,
            IVideoMediaSigner? mediaSigner = null)
        {
            _courseService = courseService;
            _progressService = progressService;
            _commentService = commentService;
            _mediaSigner = mediaSigner;
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateLesson([FromBody] Lesson lesson)
        {
            var created = await _courseService.CreateLessonAsync(lesson);
            return Ok(new { message = "Lesson created", lesson = created });
        }

        [HttpGet("{lessonId}")]
        [Authorize]
        public async Task<IActionResult> GetLesson(string lessonId)
        {
            var (authorized, lesson, _, denyMessage) = await AuthorizeLessonAccessAsync(lessonId);
            if (lesson == null) return NotFound(new { message = "Lesson not found" });
            if (!authorized)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = denyMessage });

            await _courseService.IncrementViewCountAsync(lessonId);

            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(lesson.VideoUrl) && _mediaSigner != null)
                lesson.VideoUrl = _mediaSigner.SignMediaUrl(lesson.VideoUrl, userId);
            if (!string.IsNullOrWhiteSpace(lesson.RecordingUrl) && _mediaSigner != null)
                lesson.RecordingUrl = _mediaSigner.SignMediaUrl(lesson.RecordingUrl, userId);

            return Ok(lesson);
        }

        [HttpPost("{lessonId}/media-access")]
        [Authorize]
        public async Task<IActionResult> GetLessonMediaAccess(string lessonId)
        {
            var (authorized, lesson, _, denyMessage) = await AuthorizeLessonAccessAsync(lessonId);
            if (lesson == null) return NotFound(new { message = "Lesson not found" });
            if (!authorized)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = denyMessage });

            var rawUrl = !string.IsNullOrWhiteSpace(lesson.VideoUrl)
                ? lesson.VideoUrl
                : lesson.RecordingUrl;

            if (string.IsNullOrWhiteSpace(rawUrl))
                return NotFound(new { message = "No video is attached to this lesson." });

            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            bool isLocal = _mediaSigner?.IsLocalPlatformMedia(rawUrl)
                ?? rawUrl.StartsWith("/api/v1/files/download/", StringComparison.OrdinalIgnoreCase);
            string streamUrl = _mediaSigner != null
                ? _mediaSigner.SignMediaUrl(rawUrl, userId)
                : rawUrl;

            var identityLabel = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? userId;

            return Ok(new
            {
                streamUrl,
                isLocalHosted = isLocal,
                title = lesson.Title,
                durationSeconds = lesson.DurationSeconds,
                watermarkText = $"{identityLabel} · ID:{userId}",
                expiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        }

        private async Task<(bool Authorized, Lesson? Lesson, Course? Course, string DenyMessage)> AuthorizeLessonAccessAsync(string lessonId)
        {
            var lesson = await _courseService.GetLessonByIdAsync(lessonId);
            if (lesson == null) return (false, null, null, "Lesson not found.");

            var chapter = await _courseService.GetChapterByIdAsync(lesson.ChapterId);
            var course = chapter != null ? await _courseService.GetCourseByIdAsync(chapter.CourseId) : null;

            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            bool isStaff = User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin") ||
                           (course != null && course.InstructorId == userId);

            if (isStaff || lesson.IsFree || (course != null && course.IsFree))
                return (true, lesson, course, string.Empty);

            if (course != null)
            {
                var progress = await _progressService.GetStudentProgressAsync(userId, course.Id);
                if (progress != null)
                    return (true, lesson, course, string.Empty);
            }

            return (false, lesson, course, "Please enroll in this video course to watch this lesson on the platform.");
        }

        [HttpPut("{lessonId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateLesson(string lessonId, [FromBody] Lesson lesson)
        {
            var success = await _courseService.UpdateLessonAsync(lessonId, lesson);
            return success ? Ok(new { message = "Lesson updated" }) : NotFound(new { message = "Lesson not found" });
        }

        [HttpDelete("{lessonId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteLesson(string lessonId)
        {
            var success = await _courseService.DeleteLessonAsync(lessonId);
            return success ? Ok(new { message = "Lesson deleted" }) : NotFound(new { message = "Lesson not found" });
        }

        [HttpPost("{lessonId}/progress")]
        [Authorize]
        public async Task<IActionResult> UpdateProgress(string lessonId, [FromBody] ProgressUpdate update)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _progressService.UpdateLessonProgressAsync(
                userId, update.CourseId, lessonId, update.WatchedSeconds, update.TotalSeconds);

            if (!success) return BadRequest(new { message = "Failed to update progress" });
            return Ok(new { message = "Progress updated" });
        }

        [HttpPost("{lessonId}/notes")]
        [Authorize]
        public async Task<IActionResult> AddNote(string lessonId, [FromBody] AddNoteRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var note = new Note { Timestamp = request.Timestamp, Content = request.Content, CreatedAt = DateTime.UtcNow };
            var success = await _progressService.AddNoteAsync(userId, request.CourseId, lessonId, note);
            if (!success) return BadRequest(new { message = "Failed to add note. Ensure enrolled in course." });
            return Ok(new { message = "Note added", note });
        }

        [HttpPost("{lessonId}/bookmarks")]
        [Authorize]
        public async Task<IActionResult> AddBookmark(string lessonId, [FromBody] AddBookmarkRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _progressService.AddBookmarkAsync(userId, request.CourseId, lessonId, request.Timestamp);
            if (!success) return BadRequest(new { message = "Failed to add bookmark. Ensure enrolled in course." });
            return Ok(new { message = "Bookmark added" });
        }

        [HttpGet("{lessonId}/comments")]
        public async Task<IActionResult> GetComments(string lessonId) =>
            Ok(await _commentService.GetLessonCommentsAsync(lessonId));

        [HttpPost("{lessonId}/comments")]
        [Authorize]
        public async Task<IActionResult> AddComment(string lessonId, [FromBody] AddCommentRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

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

        [HttpGet("comments/{commentId}/replies")]
        public async Task<IActionResult> GetCommentReplies(string commentId) =>
            Ok(await _commentService.GetCommentRepliesAsync(commentId));

        [HttpPost("comments/{commentId}/like")]
        [Authorize]
        public async Task<IActionResult> LikeComment(string commentId)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _commentService.LikeCommentAsync(commentId, userId);
            return success ? Ok(new { message = "Comment reaction updated" }) : NotFound(new { message = "Comment not found" });
        }

        [HttpDelete("comments/{commentId}")]
        [Authorize]
        public async Task<IActionResult> DeleteComment(string commentId)
        {
            var userId = User.FindFirst("userId")?.Value;
            bool isStaff = User.IsInRole("Teacher") || User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            var existing = await _commentService.GetCommentByIdAsync(commentId);
            if (existing == null) return NotFound(new { message = "Comment not found" });
            if (existing.StudentId != userId && !isStaff)
                return Forbid();

            var deleted = await _commentService.DeleteCommentAsync(commentId);
            return deleted ? Ok(new { message = "Comment deleted" }) : NotFound();
        }
    }

    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/videos/progress")]
    public class ProgressController : ControllerBase
    {
        private readonly ProgressTrackingService _progressService;

        public ProgressController(ProgressTrackingService progressService)
        {
            _progressService = progressService;
        }

        [HttpGet("my-courses")]
        [Authorize]
        public async Task<IActionResult> GetMyCourses()
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            return Ok(await _progressService.GetStudentCoursesAsync(userId));
        }

        [HttpGet("course/{courseId}")]
        [Authorize]
        public async Task<IActionResult> GetCourseProgress(string courseId)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var progress = await _progressService.GetStudentProgressAsync(userId, courseId);
            if (progress == null) return NotFound(new { message = "No progress found" });
            return Ok(progress);
        }

        [HttpPost("course/{courseId}/rate")]
        [Authorize]
        public async Task<IActionResult> RateCourse(string courseId, [FromBody] RateCourseRequest request)
        {
            var userId = User.FindFirst("userId")?.Value ?? string.Empty;
            var success = await _progressService.RateCourseAsync(userId, courseId, request.Rating, request.Review);
            if (!success) return BadRequest(new { message = "Failed to rate course (rating must be 1-5 and user enrolled)" });
            return Ok(new { message = "Course rated" });
        }
    }

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
