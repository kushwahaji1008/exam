using MongoDB.Driver;
using EnrollmentService.Models;
using CourseService.Models.V1;
using CourseService.Services;
using WalletService.Services;

namespace EnrollmentService.Services
{
    public class EnrollmentManager : ICourseEnrollmentLookup
    {
        private readonly IMongoCollection<Enrollment> _enrollments;
        private readonly WalletManager _walletManager;
        private readonly CourseManagementService _courseService;

        public EnrollmentManager(
            EnrollmentService.Services.MongoDbService mongoDb,
            WalletManager walletManager,
            CourseManagementService courseService)
        {
            _enrollments = mongoDb.Enrollments;
            _walletManager = walletManager;
            _courseService = courseService;
        }

        public async Task<(bool Success, string Message)> PurchaseCourseAsync(string userId, string courseId)
        {
            if (string.IsNullOrWhiteSpace(courseId))
                return (false, "Course ID is required.");

            // 1. Fetch authoritative course details from CourseService
            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return (false, "Course not found.");
            if (course.Status != CourseStatus.Published)
                return (false, "This course is not currently open for enrollment.");

            decimal truePrice = course.DiscountCoursePrice > 0 && course.DiscountCoursePrice < course.CoursePrice
                ? course.DiscountCoursePrice
                : course.CoursePrice;

            // 2. Check if already active
            var existing = await _enrollments.Find(e => e.UserId == userId && e.CourseId == courseId).FirstOrDefaultAsync();
            if (existing != null && existing.Status == EnrollmentStatus.Active)
            {
                return (false, "You already have active access to this course.");
            }

            // 3. Debit wallet if paid course (free courses bypass wallet balance failure)
            string idempotencyKey = $"ENR_BUY_{userId}_{courseId}";
            if (truePrice > 0)
            {
                var (walletSuccess, walletMessage) = await _walletManager.DebitAsync(
                    userId,
                    truePrice,
                    idempotencyKey,
                    courseId,
                    "EnrollmentModule"
                );

                if (!walletSuccess)
                {
                    return (false, walletMessage);
                }
            }

            // 4. Persist active enrollment
            if (existing == null)
            {
                var newEnrollment = new Enrollment
                {
                    UserId = userId,
                    CourseId = courseId,
                    CoinsPaid = truePrice,
                    WalletTransactionId = truePrice > 0 ? idempotencyKey : "FREE_ENROLLMENT",
                    Status = EnrollmentStatus.Active,
                    EnrolledAt = DateTime.UtcNow
                };
                await _enrollments.InsertOneAsync(newEnrollment);
            }
            else
            {
                var update = Builders<Enrollment>.Update
                    .Set(e => e.Status, EnrollmentStatus.Active)
                    .Set(e => e.CoinsPaid, truePrice)
                    .Set(e => e.WalletTransactionId, truePrice > 0 ? idempotencyKey : "FREE_ENROLLMENT")
                    .Set(e => e.EnrolledAt, DateTime.UtcNow);

                await _enrollments.UpdateOneAsync(e => e.Id == existing.Id, update);
            }

            // 5. Increment course enrollment counter
            await _courseService.IncrementEnrollmentCountAsync(courseId);

            return (true, "Course purchased successfully! Start learning now.");
        }

        public async Task<List<Enrollment>> GetMyCoursesAsync(string userId)
        {
            return await _enrollments
                .Find(e => e.UserId == userId && e.Status == EnrollmentStatus.Active)
                .SortByDescending(e => e.EnrolledAt)
                .ToListAsync();
        }

        public async Task<bool> HasAccessAsync(string userId, string courseId)
        {
            return await _enrollments
                .Find(e => e.UserId == userId && e.CourseId == courseId && e.Status == EnrollmentStatus.Active)
                .AnyAsync();
        }

        // ICourseEnrollmentLookup implementation
        public Task<bool> IsUserEnrolledAsync(string userId, string courseId) =>
            HasAccessAsync(userId, courseId);

        public async Task<List<string>> GetEnrolledCourseIdsAsync(string userId)
        {
            return await _enrollments
                .Find(e => e.UserId == userId && e.Status == EnrollmentStatus.Active)
                .Project(e => e.CourseId)
                .ToListAsync();
        }
    }
}
