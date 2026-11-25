using System.Security.Cryptography;
using MongoDB.Driver;
using AuthService.Models;

namespace AuthService.Services
{
    public class PhoneService : IPhoneService
    {
        private const int MaxPhoneOtpAttempts = 5;
        private readonly MongoDbService _mongoDb;
        private readonly ILogger<PhoneService> _logger;
        private readonly AuditService? _auditService;

        public PhoneService(MongoDbService mongoDb, ILogger<PhoneService> logger, AuditService? auditService = null)
        {
            _mongoDb = mongoDb;
            _logger = logger;
            _auditService = auditService;
        }

        private async Task<User?> FindActiveUserAsync(string userId) =>
            await _mongoDb.Users.Find(u => (u.UserId == userId || u.Id == userId) && !u.IsDeleted).FirstOrDefaultAsync();

        public async Task<(bool Success, string Message)> AddPhoneAsync(string userId, AddPhoneRequest request)
        {
            var user = await FindActiveUserAsync(userId);
            if (user == null) return (false, "User not found");

            if (!string.IsNullOrEmpty(user.Phone) && user.IsPhoneVerified)
                return (false, "Phone number already exists and is verified. Use change-phone endpoint instead.");

            var phoneExists = await _mongoDb.Users.Find(u => u.Phone == request.PhoneNumber && u.Id != user.Id && !u.IsDeleted).AnyAsync();
            if (phoneExists) return (false, "Phone number is already associated with another account.");

            string otp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Phone, request.PhoneNumber)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.PhoneOtp, otp)
                .Set(u => u.PhoneOtpExpiry, DateTime.UtcNow.AddMinutes(10))
                .Set(u => u.PhoneOtpAttemptCount, 0);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            await SendSmsAsync(request.PhoneNumber, otp);
            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "PhoneAddInitiated", $"Verification OTP sent to phone ending in {(request.PhoneNumber.Length >= 4 ? request.PhoneNumber[^4..] : request.PhoneNumber)}");

            return (true, "OTP sent to phone");
        }

        public async Task<(bool Success, string Message)> VerifyPhoneAsync(string userId, string otp)
        {
            var user = await FindActiveUserAsync(userId);
            if (user == null) return (false, "User not found");

            if (string.IsNullOrEmpty(user.Phone)) return (false, "No phone number pending verification");
            if (user.IsPhoneVerified) return (false, "Phone is already verified");
            if (string.IsNullOrEmpty(user.PhoneOtp) || user.PhoneOtpExpiry == null || DateTime.UtcNow > user.PhoneOtpExpiry)
                return (false, "Invalid or expired OTP");

            if (user.PhoneOtpAttemptCount >= MaxPhoneOtpAttempts)
            {
                await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id,
                    Builders<User>.Update.Set(u => u.PhoneOtp, null).Set(u => u.PhoneOtpExpiry, null));
                return (false, "Too many failed verification attempts. Please request a new OTP.");
            }

            if (user.PhoneOtp != otp)
            {
                await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id,
                    Builders<User>.Update.Inc(u => u.PhoneOtpAttemptCount, 1));
                return (false, "Invalid or expired OTP");
            }

            var update = Builders<User>.Update
                .Set(u => u.IsPhoneVerified, true)
                .Set(u => u.PhoneOtp, null)
                .Set(u => u.PhoneOtpExpiry, null)
                .Set(u => u.PhoneOtpAttemptCount, 0);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);
            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "PhoneVerified", "Phone number verified.");

            return (true, "Phone verified successfully");
        }

        public async Task<(bool Success, string Message)> ChangePhoneAsync(string userId, AddPhoneRequest request)
        {
            var user = await FindActiveUserAsync(userId);
            if (user == null) return (false, "User not found");

            var phoneExists = await _mongoDb.Users.Find(u => u.Phone == request.PhoneNumber && u.Id != user.Id && !u.IsDeleted).AnyAsync();
            if (phoneExists) return (false, "Phone number is already associated with another account.");

            string otp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Phone, request.PhoneNumber)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.PhoneOtp, otp)
                .Set(u => u.PhoneOtpExpiry, DateTime.UtcNow.AddMinutes(10))
                .Set(u => u.PhoneOtpAttemptCount, 0);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            await SendSmsAsync(request.PhoneNumber, otp);
            return (true, "OTP sent to new phone");
        }

        public async Task<(bool Success, string Message)> RemovePhoneAsync(string userId)
        {
            var user = await FindActiveUserAsync(userId);
            if (user == null) return (false, "User not found");

            var update = Builders<User>.Update
                .Set(u => u.Phone, null)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.PhoneOtp, null)
                .Set(u => u.PhoneOtpExpiry, null)
                .Set(u => u.PhoneOtpAttemptCount, 0);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);
            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "PhoneRemoved", "Phone number removed from account.");

            return (true, "Phone removed successfully");
        }

        public async Task<(bool Success, string Message)> ResendPhoneOtpAsync(string userId)
        {
            var user = await FindActiveUserAsync(userId);
            if (user == null) return (false, "User not found");
            if (string.IsNullOrEmpty(user.Phone)) return (false, "No phone number on file to verify");
            if (user.IsPhoneVerified) return (false, "Phone is already verified");

            string otp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.PhoneOtp, otp)
                .Set(u => u.PhoneOtpExpiry, DateTime.UtcNow.AddMinutes(10))
                .Set(u => u.PhoneOtpAttemptCount, 0);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            await SendSmsAsync(user.Phone, otp);
            return (true, "OTP resent successfully");
        }

        // --- Helper Methods ---

        private string GenerateOtp() => RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString("D6");

        private Task SendSmsAsync(string phoneNumber, string otp)
        {
            _logger.LogInformation("[SMS] Dispatched verification OTP to phone ending in {Suffix}",
                phoneNumber.Length >= 4 ? phoneNumber[^4..] : phoneNumber);
            return Task.CompletedTask;
        }
    }
}