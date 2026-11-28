using MongoDB.Driver;
using EnrollmentService.Models;
using CourseService.Models.V1;
using CourseService.Services;
using WalletService.Services;

namespace EnrollmentService.Services
{
    public class PurchaseableExamInfo
    {
        public string ExamId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal DiscountPrice { get; set; }
        public decimal EffectivePrice => DiscountPrice > 0 && DiscountPrice < Price ? DiscountPrice : Price;
        public bool IsOpenForPurchase { get; set; }
        public List<string> BlockedStudents { get; set; } = new();
    }

    public class PurchaseableSeriesInfo
    {
        public string SeriesId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal DiscountPrice { get; set; }
        public decimal EffectivePrice => DiscountPrice > 0 && DiscountPrice < Price ? DiscountPrice : Price;
        public List<string> ExamIds { get; set; } = new();
    }

    public interface IExamPurchaseCatalog
    {
        Task<PurchaseableExamInfo?> GetExamInfoAsync(string examId);
        Task<PurchaseableSeriesInfo?> GetSeriesInfoAsync(string seriesId);
        Task<HashSet<string>> GetSeriesExamIdsAsync(IEnumerable<string> seriesIds);
    }

    public class EnrollmentManager : ICourseEnrollmentLookup
    {
        private readonly IMongoCollection<Enrollment> _enrollments;
        private readonly IMongoCollection<ExamEnrollment> _examEnrollments;
        private readonly IMongoCollection<ExamSeriesEnrollment> _seriesEnrollments;
        private readonly WalletManager _walletManager;
        private readonly CourseManagementService _courseService;
        private readonly IExamPurchaseCatalog? _examCatalog;

        public EnrollmentManager(
            EnrollmentService.Services.MongoDbService mongoDb,
            WalletManager walletManager,
            CourseManagementService courseService,
            IExamPurchaseCatalog? examCatalog = null)
        {
            _enrollments = mongoDb.Enrollments;
            _examEnrollments = mongoDb.ExamEnrollments;
            _seriesEnrollments = mongoDb.ExamSeriesEnrollments;
            _walletManager = walletManager;
            _courseService = courseService;
            _examCatalog = examCatalog;
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
                    "CoursePurchase",
                    $"Purchased course: {course.Title} ({course.CourseId})"
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

        public async Task<(bool Success, string Message)> PurchaseExamAsync(string userId, string examId)
        {
            if (string.IsNullOrWhiteSpace(examId))
                return (false, "Exam ID is required.");
            if (_examCatalog == null)
                return (false, "Exam purchase catalog is not configured.");

            var exam = await _examCatalog.GetExamInfoAsync(examId);
            if (exam == null) return (false, "Exam not found.");
            if (!exam.IsOpenForPurchase)
                return (false, "This exam is not currently open for enrollment.");
            if (exam.BlockedStudents.Contains(userId))
                return (false, "You are blocked from purchasing or taking this exam.");

            var (alreadyHasAccess, _, _, _, _) = await CheckExamAccessAsync(userId, exam.ExamId);
            if (alreadyHasAccess && exam.EffectivePrice > 0)
                return (false, "You already own access to this exam.");

            var existing = await _examEnrollments.Find(e => e.UserId == userId && e.ExamId == exam.ExamId).FirstOrDefaultAsync();
            if (existing != null && existing.Status == EnrollmentStatus.Active)
                return (false, "You already own access to this exam.");

            decimal truePrice = Math.Max(0m, exam.EffectivePrice);
            string idempotencyKey = $"EXM_BUY_{userId}_{exam.ExamId}";

            if (truePrice > 0)
            {
                var (walletSuccess, walletMessage) = await _walletManager.DebitAsync(
                    userId,
                    truePrice,
                    idempotencyKey,
                    exam.ExamId,
                    "ExamPurchase",
                    $"Purchased exam: {exam.Title} ({exam.ExamId})"
                );

                if (!walletSuccess)
                    return (false, walletMessage);
            }

            if (existing == null)
            {
                await _examEnrollments.InsertOneAsync(new ExamEnrollment
                {
                    UserId = userId,
                    ExamId = exam.ExamId,
                    CoinsPaid = truePrice,
                    WalletTransactionId = truePrice > 0 ? idempotencyKey : "FREE_EXAM",
                    Status = EnrollmentStatus.Active,
                    EnrolledAt = DateTime.UtcNow
                });
            }
            else
            {
                var update = Builders<ExamEnrollment>.Update
                    .Set(e => e.Status, EnrollmentStatus.Active)
                    .Set(e => e.CoinsPaid, truePrice)
                    .Set(e => e.WalletTransactionId, truePrice > 0 ? idempotencyKey : "FREE_EXAM")
                    .Set(e => e.EnrolledAt, DateTime.UtcNow);
                await _examEnrollments.UpdateOneAsync(e => e.Id == existing.Id, update);
            }

            return (true, truePrice > 0
                ? $"Exam purchased for {truePrice} coins! You can start the exam now."
                : "Enrolled in exam successfully.");
        }

        public async Task<(bool Success, string Message)> PurchaseSeriesAsync(string userId, string seriesId)
        {
            if (string.IsNullOrWhiteSpace(seriesId))
                return (false, "Series ID is required.");
            if (_examCatalog == null)
                return (false, "Exam purchase catalog is not configured.");

            var series = await _examCatalog.GetSeriesInfoAsync(seriesId);
            if (series == null) return (false, "Exam series not found.");

            var existing = await _seriesEnrollments.Find(e => e.UserId == userId && e.SeriesId == series.SeriesId).FirstOrDefaultAsync();
            if (existing != null && existing.Status == EnrollmentStatus.Active)
                return (false, "You already own this exam series.");

            decimal truePrice = Math.Max(0m, series.EffectivePrice);
            string idempotencyKey = $"EXS_BUY_{userId}_{series.SeriesId}";

            if (truePrice > 0)
            {
                var (walletSuccess, walletMessage) = await _walletManager.DebitAsync(
                    userId,
                    truePrice,
                    idempotencyKey,
                    series.SeriesId,
                    "ExamSeriesPurchase",
                    $"Purchased exam series: {series.Title} ({series.SeriesId})"
                );

                if (!walletSuccess)
                    return (false, walletMessage);
            }

            if (existing == null)
            {
                await _seriesEnrollments.InsertOneAsync(new ExamSeriesEnrollment
                {
                    UserId = userId,
                    SeriesId = series.SeriesId,
                    CoinsPaid = truePrice,
                    WalletTransactionId = truePrice > 0 ? idempotencyKey : "FREE_SERIES",
                    Status = EnrollmentStatus.Active,
                    EnrolledAt = DateTime.UtcNow
                });
            }
            else
            {
                var update = Builders<ExamSeriesEnrollment>.Update
                    .Set(e => e.Status, EnrollmentStatus.Active)
                    .Set(e => e.CoinsPaid, truePrice)
                    .Set(e => e.WalletTransactionId, truePrice > 0 ? idempotencyKey : "FREE_SERIES")
                    .Set(e => e.EnrolledAt, DateTime.UtcNow);
                await _seriesEnrollments.UpdateOneAsync(e => e.Id == existing.Id, update);
            }

            return (true, truePrice > 0
                ? $"Exam series purchased for {truePrice} coins! All linked exams are unlocked."
                : "Enrolled in exam series successfully.");
        }

        public async Task<List<Enrollment>> GetMyCoursesAsync(string userId)
        {
            return await _enrollments
                .Find(e => e.UserId == userId && e.Status == EnrollmentStatus.Active)
                .SortByDescending(e => e.EnrolledAt)
                .ToListAsync();
        }

        public async Task<List<ExamEnrollment>> GetMyExamsAsync(string userId)
        {
            return await _examEnrollments
                .Find(e => e.UserId == userId && e.Status == EnrollmentStatus.Active)
                .SortByDescending(e => e.EnrolledAt)
                .ToListAsync();
        }

        public async Task<List<ExamSeriesEnrollment>> GetMySeriesAsync(string userId)
        {
            return await _seriesEnrollments
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

        public async Task<(bool HasAccess, bool IsPurchased, decimal Price, decimal DiscountPrice, decimal EffectivePrice)> CheckExamAccessAsync(string userId, string examId)
        {
            var direct = await _examEnrollments
                .Find(e => e.UserId == userId && e.ExamId == examId && e.Status == EnrollmentStatus.Active)
                .AnyAsync();

            var info = _examCatalog != null ? await _examCatalog.GetExamInfoAsync(examId) : null;
            decimal price = info?.Price ?? 0m;
            decimal discountPrice = info?.DiscountPrice ?? 0m;
            decimal effectivePrice = info?.EffectivePrice ?? 0m;
            var canonicalExamId = info?.ExamId ?? examId;

            if (!direct && canonicalExamId != examId)
            {
                direct = await _examEnrollments
                    .Find(e => e.UserId == userId && e.ExamId == canonicalExamId && e.Status == EnrollmentStatus.Active)
                    .AnyAsync();
            }

            if (direct)
                return (true, true, price, discountPrice, effectivePrice);

            if (_examCatalog != null)
            {
                var mySeriesIds = await _seriesEnrollments
                    .Find(e => e.UserId == userId && e.Status == EnrollmentStatus.Active)
                    .Project(e => e.SeriesId)
                    .ToListAsync();
                if (mySeriesIds.Count > 0)
                {
                    var unlockedIds = await _examCatalog.GetSeriesExamIdsAsync(mySeriesIds);
                    if (unlockedIds.Contains(canonicalExamId))
                        return (true, true, price, discountPrice, effectivePrice);
                }
            }

            bool freeAccess = effectivePrice <= 0;
            return (freeAccess, false, price, discountPrice, effectivePrice);
        }

        public async Task<bool> HasSeriesAccessAsync(string userId, string seriesId)
        {
            return await _seriesEnrollments
                .Find(e => e.UserId == userId && e.SeriesId == seriesId && e.Status == EnrollmentStatus.Active)
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
