using MongoDB.Driver;
using EnrollmentService.Models;
using EnrollmentService.Clients;

namespace EnrollmentService.Services
{
    public class EnrollmentManager
    {
        private readonly IMongoCollection<Enrollment> _enrollments;
        private readonly WalletServiceClient _walletClient;

        public EnrollmentManager(MongoDbService mongoDb, WalletServiceClient walletClient)
        {
            _enrollments = mongoDb.Enrollments;
            _walletClient = walletClient;
        }

        public async Task<(bool Success, string Message)> PurchaseCourseAsync(string userId, string courseId, decimal coursePrice)
        {
            // 1. Check if already active
            var existing = await _enrollments.Find(e => e.UserId == userId && e.CourseId == courseId).FirstOrDefaultAsync();
            if (existing != null && existing.Status == EnrollmentStatus.Active)
            {
                return (false, "You already have active access to this course.");
            }

            // 2. Generate a strict Idempotency Key (Format: ENROLL_UserId_CourseId)
            // Even if the user clicks 10 times, WalletService will only deduct coins ONCE for this exact string.
            string idempotencyKey = $"ENR_BUY_{userId}_{courseId}";

            // 3. Call WalletService to deduct coins
            var walletResponse = await _walletClient.DebitCoinsAsync(userId, coursePrice, idempotencyKey, courseId);

            if (!walletResponse.Success)
            {
                return (false, walletResponse.Message); // Return "Insufficient balance" directly to user
            }

            // 4. Wallet transaction successful, mark course as Active
            if (existing == null)
            {
                var newEnrollment = new Enrollment
                {
                    UserId = userId,
                    CourseId = courseId,
                    CoinsPaid = coursePrice,
                    WalletTransactionId = idempotencyKey,
                    Status = EnrollmentStatus.Active
                };
                await _enrollments.InsertOneAsync(newEnrollment);
            }
            else
            {
                var update = Builders<Enrollment>.Update
                    .Set(e => e.Status, EnrollmentStatus.Active)
                    .Set(e => e.CoinsPaid, coursePrice)
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

        // Extremely fast method used by Video Player or Exam Engine to check access
        public async Task<bool> HasAccessAsync(string userId, string courseId)
        {
            return await _enrollments
                .Find(e => e.UserId == userId && e.CourseId == courseId && e.Status == EnrollmentStatus.Active)
                .AnyAsync();
        }
    }
}