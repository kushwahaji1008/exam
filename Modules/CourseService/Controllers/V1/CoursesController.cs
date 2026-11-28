using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CourseService.Models.V1;
using CourseService.Services;
using System.Security.Claims;

namespace CourseService.Controllers.V1
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/courses")]
    public class CoursesController : ControllerBase
    {
        private readonly CourseManagementService _courseService;
        private readonly ICourseEnrollmentLookup? _enrollmentLookup;
        private readonly ICourseMediaSigner? _mediaSigner;

        public CoursesController(
            CourseManagementService courseService,
            ICourseEnrollmentLookup? enrollmentLookup = null,
            ICourseMediaSigner? mediaSigner = null)
        {
            _courseService = courseService;
            _enrollmentLookup = enrollmentLookup;
            _mediaSigner = mediaSigner;
        }

        private string? GetCurrentUserId() => User.FindFirst("userId")?.Value;
        private string? GetCurrentUserRole() => User.FindFirst(ClaimTypes.Role)?.Value;

        // ==========================================
        // 1. CORE CRUD & LIFECYCLE
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> GetAllCourses()
        {
            var courses = await _courseService.GetAllCoursesAsync();
            return Ok(courses);
        }

        [HttpPost]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> CreateCourse([FromBody] CreateCourseRequest request)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var course = await _courseService.CreateCourseAsync(request, userId);
            return CreatedAtAction(nameof(GetCourseById), new { courseId = course.CourseId }, course);
        }

        [HttpGet("{courseId}")]
        public async Task<IActionResult> GetCourseById(string courseId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return NotFound();

            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();
            bool hasCourseAccess = false;
            if (!string.IsNullOrEmpty(userId))
            {
                bool isStaff = role is "Teacher" or "Admin" or "SuperAdmin" ||
                               course.CreatedBy == userId ||
                               course.InstructorIds.Contains(userId);
                hasCourseAccess = isStaff ||
                                  course.CoursePrice == 0 ||
                                  (_enrollmentLookup != null && await _enrollmentLookup.IsUserEnrolledAsync(userId, courseId));
            }

            // Redact protected ContentUrl values for unauthorized visitors so media cannot be scraped directly.
            var safeSections = course.Sections.Select(s => new CourseSection
            {
                Id = s.Id,
                Title = s.Title,
                OrderIndex = s.OrderIndex,
                Items = s.Items.Select(i => new CurriculumItem
                {
                    Id = i.Id,
                    Title = i.Title,
                    Type = i.Type,
                    ContentUrl = (hasCourseAccess || (!string.IsNullOrEmpty(userId) && i.IsFreePreview)) ? i.ContentUrl : string.Empty,
                    OrderIndex = i.OrderIndex,
                    IsFreePreview = i.IsFreePreview,
                    DurationSeconds = i.DurationSeconds
                }).ToList()
            }).ToList();

            return Ok(new Course
            {
                Id = course.Id,
                CourseId = course.CourseId,
                Title = course.Title,
                Description = course.Description,
                ThumbnailUrl = course.ThumbnailUrl,
                Status = course.Status,
                Level = course.Level,
                CoursePrice = course.CoursePrice,
                DiscountCoursePrice = course.DiscountCoursePrice,
                EnrollmentCount = course.EnrollmentCount,
                CreatedBy = course.CreatedBy,
                InstructorIds = course.InstructorIds,
                CreatedAt = course.CreatedAt,
                UpdatedAt = course.UpdatedAt,
                Sections = safeSections
            });
        }

        [HttpPost("{courseId}/items/{itemId}/media-access")]
        [Authorize]
        public async Task<IActionResult> GetItemMediaAccess(string courseId, string itemId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return NotFound(new { message = "Course not found." });

            var item = course.Sections.SelectMany(s => s.Items).FirstOrDefault(i => i.Id == itemId);
            if (item == null) return NotFound(new { message = "Curriculum item not found." });

            var role = GetCurrentUserRole();
            bool isStaff = role is "Teacher" or "Admin" or "SuperAdmin" ||
                           course.CreatedBy == userId ||
                           course.InstructorIds.Contains(userId);
            bool hasAccess = isStaff ||
                             item.IsFreePreview ||
                             course.CoursePrice == 0 ||
                             (_enrollmentLookup != null && await _enrollmentLookup.IsUserEnrolledAsync(userId, courseId));

            if (!hasAccess)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Enroll in this course to open this material on the platform." });

            if (string.IsNullOrWhiteSpace(item.ContentUrl))
                return NotFound(new { message = "No media content is attached to this item." });

            bool isLocal = _mediaSigner?.IsLocalPlatformMedia(item.ContentUrl)
                ?? item.ContentUrl.StartsWith("/api/v1/files/download/", StringComparison.OrdinalIgnoreCase);
            string streamUrl = _mediaSigner != null
                ? _mediaSigner.SignMediaUrl(item.ContentUrl, userId)
                : item.ContentUrl;

            var identityLabel = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? userId;

            return Ok(new
            {
                streamUrl,
                isLocalHosted = isLocal,
                itemType = item.Type,
                title = item.Title,
                durationSeconds = item.DurationSeconds,
                watermarkText = $"{identityLabel} · ID:{userId}",
                expiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        }

        [HttpPatch("{courseId}")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateCourse(string courseId, [FromBody] object request)
        {
            var success = await _courseService.PatchCourseAsync(courseId, request);
            return success ? Ok(new { message = "Course updated" }) : BadRequest(new { message = "Course not found or invalid update payload" });
        }

        [HttpDelete("{courseId}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> DeleteCourse(string courseId)
        {
            var success = await _courseService.DeleteCourseAsync(courseId);
            return success ? Ok(new { message = "Course deleted" }) : NotFound();
        }

        [HttpPost("{courseId}/publish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> PublishCourse(string courseId)
        {
            var success = await _courseService.ChangeCourseStatusAsync(courseId, CourseStatus.Published);
            return success ? Ok(new { message = "Course published" }) : NotFound(new { message = "Course not found" });
        }

        [HttpPost("{courseId}/unpublish")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> UnpublishCourse(string courseId)
        {
            var success = await _courseService.ChangeCourseStatusAsync(courseId, CourseStatus.Draft);
            return success ? Ok(new { message = "Course unpublished" }) : NotFound(new { message = "Course not found" });
        }

        // ==========================================
        // 2. DISCOVERY (Public)
        // ==========================================

        [HttpGet("featured")]
        public async Task<IActionResult> GetFeaturedCourses([FromQuery] int limit = 10)
        {
            var courses = await _courseService.GetFeaturedCoursesAsync(limit);
            return Ok(new { courses });
        }

        [HttpGet("popular")]
        public async Task<IActionResult> GetPopularCourses([FromQuery] int limit = 10)
        {
            var courses = await _courseService.GetPopularCoursesAsync(limit);
            return Ok(new { courses });
        }

        [HttpGet("recommended")]
        public async Task<IActionResult> GetRecommendedCourses([FromQuery] int limit = 10)
        {
            var userId = GetCurrentUserId();
            var enrolledIds = (!string.IsNullOrEmpty(userId) && _enrollmentLookup != null)
                ? await _enrollmentLookup.GetEnrolledCourseIdsAsync(userId)
                : new List<string>();

            var courses = await _courseService.GetRecommendedCoursesAsync(enrolledIds, limit);
            return Ok(new { courses });
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchCourses(
            [FromQuery] string? query = null,
            [FromQuery] CourseLevel? level = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var (results, total) = await _courseService.SearchCoursesAsync(query, level, page, pageSize);
            return Ok(new { results, total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 100), query });
        }

        // ==========================================
        // 3. STUDENT PORTAL
        // ==========================================

        [HttpGet("{courseId}/overview")]
        public async Task<IActionResult> GetCourseOverview(string courseId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return NotFound(new { message = "Course not found" });

            var totalItems = course.Sections.Sum(s => s.Items.Count);
            var totalDurationSeconds = course.Sections.SelectMany(s => s.Items).Sum(i => i.DurationSeconds);

            return Ok(new
            {
                courseId = course.CourseId,
                title = course.Title,
                description = course.Description,
                level = course.Level,
                status = course.Status,
                coursePrice = course.CoursePrice,
                discountCoursePrice = course.DiscountCoursePrice,
                enrollmentCount = course.EnrollmentCount,
                sectionCount = course.Sections.Count,
                totalItems,
                totalDurationSeconds,
                syllabus = course.Sections.OrderBy(s => s.OrderIndex).Select(s => new
                {
                    s.Id,
                    s.Title,
                    s.OrderIndex,
                    itemCount = s.Items.Count,
                    items = s.Items.OrderBy(i => i.OrderIndex).Select(i => new
                    {
                        i.Id,
                        i.Title,
                        i.Type,
                        i.IsFreePreview,
                        i.DurationSeconds,
                        i.OrderIndex
                    })
                })
            });
        }

        [HttpGet("{courseId}/dashboard")]
        [Authorize]
        public async Task<IActionResult> GetStudentDashboard(string courseId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return NotFound(new { message = "Course not found" });

            var role = GetCurrentUserRole();
            bool isStaff = role is "Admin" or "SuperAdmin" || course.CreatedBy == userId || course.InstructorIds.Contains(userId);
            bool isEnrolled = isStaff || (_enrollmentLookup != null && await _enrollmentLookup.IsUserEnrolledAsync(userId, courseId));

            var orderedItems = course.Sections
                .OrderBy(s => s.OrderIndex)
                .SelectMany(s => s.Items.OrderBy(i => i.OrderIndex))
                .ToList();

            return Ok(new
            {
                courseId = course.CourseId,
                title = course.Title,
                hasAccess = isEnrolled,
                totalSections = course.Sections.Count,
                totalItems = orderedItems.Count,
                totalDurationSeconds = orderedItems.Sum(i => i.DurationSeconds),
                nextItem = orderedItems.FirstOrDefault()?.Title
            });
        }

        [HttpGet("{courseId}/access")]
        [Authorize]
        public async Task<IActionResult> CheckCourseAccess(string courseId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return NotFound(new { message = "Course not found" });

            var role = GetCurrentUserRole();
            bool isAdminOrInstructor = role is "Admin" or "SuperAdmin" ||
                                       course.CreatedBy == userId ||
                                       course.InstructorIds.Contains(userId);

            bool hasAccess = isAdminOrInstructor ||
                             course.CoursePrice == 0 ||
                             (_enrollmentLookup != null && await _enrollmentLookup.IsUserEnrolledAsync(userId, courseId));

            return Ok(new { hasAccess, role = role ?? "Student" });
        }

        // ==========================================
        // 4. ADMIN & ANALYTICS
        // ==========================================

        [HttpGet("{courseId}/stats")]
        [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
        public async Task<IActionResult> GetCourseStats(string courseId)
        {
            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return NotFound(new { message = "Course not found" });

            var effectivePrice = course.DiscountCoursePrice > 0 && course.DiscountCoursePrice < course.CoursePrice
                ? course.DiscountCoursePrice
                : course.CoursePrice;

            return Ok(new
            {
                courseId = course.CourseId,
                title = course.Title,
                status = course.Status.ToString(),
                enrolledStudents = course.EnrollmentCount,
                effectivePrice,
                estimatedRevenue = course.EnrollmentCount * effectivePrice,
                sectionCount = course.Sections.Count,
                totalItems = course.Sections.Sum(s => s.Items.Count)
            });
        }
    }
}
