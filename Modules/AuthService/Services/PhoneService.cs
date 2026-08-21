using MongoDB.Driver;
using AuthService.Models;

namespace AuthService.Services
{
    public class PhoneService : IPhoneService
    {
        private readonly MongoDbService _mongoDb;
        private readonly ILogger<PhoneService> _logger;

        public PhoneService(MongoDbService mongoDb, ILogger<PhoneService> logger)
        {
            _mongoDb = mongoDb;
            _logger = logger;
        }

        public async Task<(bool Success, string Message)> AddPhoneAsync(string userId, AddPhoneRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");
            
            if (!string.IsNullOrEmpty(user.Phone) && user.IsPhoneVerified)
                return (false, "Phone number already exists and is verified. Use change-phone endpoint instead.");

            // Check if phone is already associated with another active account
            var phoneExists = await _mongoDb.Users.Find(u => u.Phone == request.PhoneNumber && u.Id != userId && !u.IsDeleted).AnyAsync();
            if (phoneExists) return (false, "Phone number is already associated with another account.");

            string otp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Phone, request.PhoneNumber)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.Otp, otp)
                .Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(10));

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);

            await SendSmsAsync(request.PhoneNumber, otp);
            return (true, "OTP sent to phone");
        }

        public async Task<(bool Success, string Message)> VerifyPhoneAsync(string userId, string otp)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");

            if (string.IsNullOrEmpty(user.Phone)) return (false, "No phone number pending verification");
            if (user.IsPhoneVerified) return (false, "Phone is already verified");
            if (user.Otp != otp || DateTime.UtcNow > user.OtpExpiry) return (false, "Invalid or expired OTP");

            var update = Builders<User>.Update
                .Set(u => u.IsPhoneVerified, true)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return (true, "Phone verified successfully");
        }

        public async Task<(bool Success, string Message)> ChangePhoneAsync(string userId, AddPhoneRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");

            var phoneExists = await _mongoDb.Users.Find(u => u.Phone == request.PhoneNumber && u.Id != userId && !u.IsDeleted).AnyAsync();
            if (phoneExists) return (false, "Phone number is already associated with another account.");

            string otp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Phone, request.PhoneNumber) 
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.Otp, otp)
                .Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(10));

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);

            await SendSmsAsync(request.PhoneNumber, otp);
            return (true, "OTP sent to new phone");
        }

        public async Task<(bool Success, string Message)> RemovePhoneAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");

            var update = Builders<User>.Update
                .Set(u => u.Phone, null)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return (true, "Phone removed successfully");
        }

        public async Task<(bool Success, string Message)> ResendPhoneOtpAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");
            if (string.IsNullOrEmpty(user.Phone)) return (false, "No phone number on file to verify");
            if (user.IsPhoneVerified) return (false, "Phone is already verified");

            string otp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Otp, otp)
                .Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(10));

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);

            await SendSmsAsync(user.Phone, otp);
            return (true, "OTP resent successfully");
        }

        // --- Helper Methods ---

        private string GenerateOtp() => new Random().Next(100000, 999999).ToString();

        private Task SendSmsAsync(string phoneNumber, string otp)
        {
            // TODO: Implement actual SMS logic using Twilio, AWS SNS, Vonage, etc.
            _logger.LogInformation($"[MOCK SMS] Sent OTP {otp} to {phoneNumber}");
            return Task.CompletedTask;
        }
    }
}