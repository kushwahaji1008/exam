using MongoDB.Driver;
using EnrollmentService.Models;
using CourseService.Services; 
using WalletService.Services; 

namespace EnrollmentService.Services
{
    public class EnrollmentManager
    {
        private readonly IMongoCollection<Enrollment> _enrollments;
        private readonly WalletManager _walletManager;
        private readonly CourseManagementService _courseService;

        // 👇 YAHAN FIX HUA HAAI: Explicitly bataya gaya hai ki EnrollmentService wala MongoDbService use karna hai
        public EnrollmentManager(
            EnrollmentService.Services.MongoDbService mongoDb, 
            WalletManager walletManager, 
            CourseManagementService courseService)
        {
            _enrollments = mongoDb.Enrollments;
            _walletManager = walletManager;
            _courseService = courseService;
        }

        // 👇 Frontend se 'coursePrice' hataya, ab sirf courseId aayega
        public async Task<(bool Success, string Message)> PurchaseCourseAsync(string userId, string courseId) 
        {
            // 1. Direct Call: CourseService se true price nikalein (Hacker proof)
            var course = await _courseService.GetCourseByIdAsync(courseId);
            if (course == null) return (false, "Course not found.");
            
            decimal truePrice = course.CoursePrice; // Aapke model ke hisaab se CoursePrice use kiya hai

            // 2. Check if already active
            var existing = await _enrollments.Find(e => e.UserId == userId && e.CourseId == courseId).FirstOrDefaultAsync();
            if (existing != null && existing.Status == EnrollmentStatus.Active)
            {
                return (false, "You already have active access to this course.");
            }

            // 3. Generate strict Idempotency Key
            string idempotencyKey = $"ENR_BUY_{userId}_{courseId}";

            // 4. Direct Call: WalletManager se coins deduct karein (0 ms delay)
            var (walletSuccess, walletMessage) = await _walletManager.DebitAsync(
                userId, 
                truePrice, 
                idempotencyKey, 
                courseId, 
                "EnrollmentModule"
            );

            if (!walletSuccess)
            {
                return (false, walletMessage); // "Insufficient balance"
            }

            // 5. Wallet deduction successful, mark course as Active
            if (existing == null)
            {
                var newEnrollment = new Enrollment
                {
                    UserId = userId,
                    CourseId = courseId,
                    CoinsPaid = truePrice,
                    WalletTransactionId = idempotencyKey,
                    Status = EnrollmentStatus.Active
                };
                await _enrollments.InsertOneAsync(newEnrollment);
            }
            else
            {
                var update = Builders<Enrollment>.Update
                    .Set(e => e.Status, EnrollmentStatus.Active)
                    .Set(e => e.CoinsPaid, truePrice)
                    .Set(e => e.WalletTransactionId, idempotencyKey);

                await _enrollments.UpdateOneAsync(e => e.Id == existing.Id, update);
            }

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
    }
}