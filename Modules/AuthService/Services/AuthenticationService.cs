using MongoDB.Driver;
using AuthService.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;

namespace AuthService.Services
{
    public class AuthenticationService
    {
        private readonly MongoDbService _mongoDb;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;

        public AuthenticationService(MongoDbService mongoDb, IConfiguration configuration, IEmailService emailService)
        {
            _mongoDb = mongoDb;
            _configuration = configuration;
            _emailService = emailService;
        }

        // ==========================================
        // 1. REGISTRATION & OTP VERIFICATION
        // ==========================================
        public async Task<(bool Success, string Message)> RegisterAsync(RegisterRequest request)
        {
            var existingUser = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).FirstOrDefaultAsync();

            if (existingUser != null && existingUser.IsEmailVerified)
                return (false, "User with this email is already registered and verified.");

            string generatedOtp = GenerateOtp();
            DateTime expiryTime = DateTime.UtcNow.AddMinutes(10);
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            if (existingUser != null && !existingUser.IsEmailVerified)
            {
                var update = Builders<User>.Update
                    .Set(u => u.Otp, generatedOtp)
                    .Set(u => u.OtpExpiry, expiryTime)
                    .Set(u => u.PasswordHash, hashedPassword)
                    .Set(u => u.FullName, request.FullName)
                    .Set(u => u.Role, request.Role)
                    .Set(u => u.Phone, request.Phone);

                await _mongoDb.Users.UpdateOneAsync(u => u.Id == existingUser.Id, update);
            }
            else
            {
                var user = new User
                {
                    Email = request.Email,
                    UserName = request.Email.Split('@')[0],
                    PasswordHash = hashedPassword,
                    FullName = request.FullName,
                    Phone = request.Phone,
                    Role = request.Role,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    IsEmailVerified = false,
                    Otp = generatedOtp,
                    OtpExpiry = expiryTime,
                    Sessions = new List<ActiveSession>()
                };
                await _mongoDb.Users.InsertOneAsync(user);
            }

            await SendOtpEmailAsync(request.Email, generatedOtp);
            return (true, "Registration initiated. Please check your email for the OTP.");
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> VerifyOtpAsync(VerifyOtpRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).FirstOrDefaultAsync();

            if (user == null) return (false, "User not found.", string.Empty, string.Empty, null);
            if (user.IsEmailVerified && request.Otp != user.Otp) return (false, "Account is already verified. Please login.", string.Empty, string.Empty, null);
            if (user.Otp != request.Otp) return (false, "Invalid verification code.", string.Empty, string.Empty, null);
            if (DateTime.UtcNow > user.OtpExpiry) return (false, "Verification code has expired. Please register again.", string.Empty, string.Empty, null);

            var newSession = CreateSession();
            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.Add(newSession);

            var update = Builders<User>.Update
                .Set(u => u.IsEmailVerified, true)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null)
                .Set(u => u.LastLoginAt, DateTime.UtcNow)
                .Set(u => u.Sessions, activeSessions);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            string token = GenerateJwtToken(user);
            return (true, "Verification successful.", token, newSession.RefreshToken, ToUserDto(user));
        }

        // ==========================================
        // 2. LOGIN & LOGOUT
        // ==========================================
        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> LoginAsync(LoginRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).FirstOrDefaultAsync();

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                if (user != null) await HandleFailedLoginAttempt(user);
                return (false, "Invalid email or password", string.Empty, string.Empty, null);
            }

            if (!user.IsEmailVerified) return (false, "Please verify your email address before logging in.", string.Empty, string.Empty, null);
            if (!user.IsActive) return (false, "Account is deactivated. Contact support.", string.Empty, string.Empty, null);
            if (user.IsLocked && user.LockoutEnd > DateTime.UtcNow) return (false, $"Account locked until {user.LockoutEnd}. Try again later.", string.Empty, string.Empty, null);

            if (user.MfaEnabled)
            {
                // In a real flow, return a temporary token used ONLY for MFA challenge endpoint
                return (false, "MFA_REQUIRED", string.Empty, string.Empty, ToUserDto(user));
            }

            var newSession = CreateSession();
            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.Add(newSession);

            var update = Builders<User>.Update
                .Set(u => u.LastLoginAt, DateTime.UtcNow)
                .Set(u => u.FailedLoginAttempts, 0)
                .Set(u => u.IsLocked, false)
                .Set(u => u.LockoutEnd, null)
                .Set(u => u.Sessions, activeSessions);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            string token = GenerateJwtToken(user);
            return (true, "Login successful", token, newSession.RefreshToken, ToUserDto(user));
        }

        public async Task<bool> LogoutAsync(string userId)
        {
            var update = Builders<User>.Update.Set(u => u.Sessions, new List<ActiveSession>());
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 3. PASSWORD MANAGEMENT & REFRESH TOKENS
        // ==========================================
        public async Task<(bool Success, string Message)> ResendOtpAsync(string email, bool isPasswordReset = false)
        {
            var user = await _mongoDb.Users.Find(u => u.Email == email && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found.");
            if (!isPasswordReset && user.IsEmailVerified) return (false, "Account is already verified.");

            string generatedOtp = GenerateOtp();
            var update = Builders<User>.Update.Set(u => u.Otp, generatedOtp).Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(10));
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            await SendOtpEmailAsync(email, generatedOtp, isPasswordReset ? "Password Reset Verification" : "Verify Your Account");
            return (true, "OTP sent successfully. Please check your email.");
        }

        public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || user.Otp != request.Otp || DateTime.UtcNow > user.OtpExpiry)
                return (false, "Invalid or expired verification code.");

            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, hashedPassword)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null)
                .Set(u => u.Sessions, new List<ActiveSession>());

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);
            return (true, "Password has been reset successfully. You can now log in.");
        }

        public async Task<(bool Success, string Message)> ChangePasswordAsync(string userId, ChangePasswordRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found.");
            if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash)) return (false, "Incorrect current password.");

            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            var update = Builders<User>.Update.Set(u => u.PasswordHash, hashedPassword);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);
            return (true, "Password changed successfully.");
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> RefreshTokenAsync(RefreshTokenRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Sessions.Any(s => s.RefreshToken == request.RefreshToken) && !u.IsDeleted).FirstOrDefaultAsync();

            if (user == null) return (false, "Invalid token.", string.Empty, string.Empty, null);

            var existingSession = user.Sessions.First(s => s.RefreshToken == request.RefreshToken);
            if (existingSession.Expiry <= DateTime.UtcNow) return (false, "Refresh token expired. Please log in again.", string.Empty, string.Empty, null);

            string newJwtToken = GenerateJwtToken(user);
            var newSession = CreateSession();

            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.RemoveAll(s => s.RefreshToken == request.RefreshToken);
            activeSessions.Add(newSession);

            var update = Builders<User>.Update.Set(u => u.Sessions, activeSessions);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            return (true, "Token refreshed successfully", newJwtToken, newSession.RefreshToken, ToUserDto(user));
        }

        // ==========================================
        // 4. USER PROFILE MANAGEMENT
        // ==========================================
        public async Task<UserDto?> GetUserByIdAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            return user != null ? ToUserDto(user) : null;
        }

        public async Task<List<UserDto>> GetAllUsersAsync()
        {
            var users = await _mongoDb.Users.Find(u => !u.IsDeleted).ToListAsync();
            return users.Select(ToUserDto).ToList();
        }

        public async Task<bool> UpdateUserProfileAsync(string userId, UpdateProfileRequest request)
        {
            var update = Builders<User>.Update
                .Set(u => u.FullName, request.FullName)
                .Set(u => u.Phone, request.Phone)
                .Set(u => u.Gender, request.Gender)
                .Set(u => u.DateOfBirth, request.DateOfBirth)
                .Set(u => u.ProfilePicture, request.ProfilePicture);

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            var update = Builders<User>.Update.Set(u => u.IsDeleted, true).Set(u => u.IsActive, false);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> CheckEmailExistsAsync(string email)
        {
            return await _mongoDb.Users.Find(u => u.Email == email && !u.IsDeleted).AnyAsync();
        }

        public async Task<bool> CheckUsernameExistsAsync(string username)
        {
            return await _mongoDb.Users.Find(u => u.UserName == username && !u.IsDeleted).AnyAsync();
        }

        // ==========================================
        // 5. EMAIL & PHONE MANAGEMENT
        // ==========================================
        public async Task<(bool Success, string Message)> ChangeEmailAsync(string userId, ChangeEmailRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)) return (false, "Incorrect password");

            var emailExists = await CheckEmailExistsAsync(request.NewEmail);
            if (emailExists) return (false, "Email is already in use by another account");

            string generatedOtp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Email, request.NewEmail) // Optionally store in a "PendingEmail" field instead
                .Set(u => u.IsEmailVerified, false)
                .Set(u => u.Otp, generatedOtp)
                .Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(15));

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);
            await SendOtpEmailAsync(request.NewEmail, generatedOtp, "Verify Your New Email");

            return (true, "Email updated. Please check your new email for a verification code.");
        }

        public async Task<(bool Success, string Message)> AddOrChangePhoneAsync(string userId, AddPhoneRequest request)
        {
            string generatedOtp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Phone, request.PhoneNumber)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.Otp, generatedOtp)
                .Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(10));

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);

            // TODO: Integrate SMS provider (Twilio/AWS SNS) here
            // await _smsService.SendSmsAsync(request.PhoneNumber, $"Your verification code is {generatedOtp}");

            return result.ModifiedCount > 0 ? (true, "OTP sent to phone") : (false, "Failed to update phone");
        }

        public async Task<(bool Success, string Message)> VerifyPhoneAsync(string userId, string otp)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || user.Otp != otp || DateTime.UtcNow > user.OtpExpiry)
                return (false, "Invalid or expired OTP");

            var update = Builders<User>.Update.Set(u => u.IsPhoneVerified, true).Set(u => u.Otp, null).Set(u => u.OtpExpiry, null);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);

            return (true, "Phone verified successfully");
        }

        public async Task<bool> RemovePhoneAsync(string userId)
        {
            var update = Builders<User>.Update.Set(u => u.Phone, null).Set(u => u.IsPhoneVerified, false);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 6. MULTI-FACTOR AUTHENTICATION (MFA)
        // ==========================================
        public async Task<(bool Success, string Message, string? Secret, string? QrCodeUrl)> SetupMfaAsync(string userId)
        {
            // Note: This requires a library like Otp.NET and QRCoder in real life
            string mockSecret = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32)
            )
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")
            .Substring(0, 16)
            .ToUpper();
            string mockQrUrl = $"otpauth://totp/ExamApp:{userId}?secret={mockSecret}&issuer=ExamApp";

            var update = Builders<User>.Update.Set(u => u.MfaSecret, mockSecret);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);

            return (true, "MFA setup initialized", mockSecret, mockQrUrl);
        }

        public async Task<(bool Success, string Message)> VerifyAndEnableMfaAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || string.IsNullOrEmpty(user.MfaSecret)) return (false, "MFA setup not initialized");

            // TODO: Validate TOTP code using Otp.NET
            bool isValidCode = code == "123456"; // Mock validation

            if (!isValidCode) return (false, "Invalid authenticator code");

            var recoveryCodes = GenerateRecoveryCodes();
            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, true)
                .Set(u => u.MfaRecoveryCodes, recoveryCodes);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return (true, "MFA enabled successfully");
        }

        public async Task<(bool Success, string Message)> DisableMfaAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, false)
                .Set(u => u.MfaSecret, null)
                .Set(u => u.MfaRecoveryCodes, new List<string>());

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0 ? (true, "MFA disabled") : (false, "Failed to disable MFA");
        }

        // public async Task<List<string>?> GetRecoveryCodesAsync(string userId)
        // {
        //     var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
        //     return user?.MfaRecoveryCodes;
        // }
        public async Task<object?> GetMfaInfoAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return null;

            return new
            {
                enabled = user.MfaEnabled,
                methods = user.MfaEnabled ? new[] { "Authenticator" } : Array.Empty<string>()
            };
        }

        public async Task<bool> GetMfaStatusAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            return user?.MfaEnabled ?? false;
        }

        public async Task<(bool Success, string Message, string? Secret, string? QrCodeUrl)> SetupMfaAsync(string userId, string method)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found", null, null);
            if (user.MfaEnabled) return (false, "MFA is already enabled", null, null);

            // Mock Base32 Secret generation (In production use Otp.NET to generate standard Base32 strings)
            string secret = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Substring(0, 16).ToUpper().Replace("=", "").Replace("/", "").Replace("+", "");

            // Format for Authenticator apps (Google Authenticator, Authy, etc.)
            string qrCodeUrl = $"otpauth://totp/ExamApp:{user.Email}?secret={secret}&issuer=ExamApp";

            var update = Builders<User>.Update.Set(u => u.MfaSecret, secret);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);

            return (true, "MFA setup initialized", secret, qrCodeUrl);
        }

        public async Task<(bool Success, string Message)> VerifyMfaSetupAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || string.IsNullOrEmpty(user.MfaSecret)) return (false, "MFA setup not initialized");

            // TODO: Use Otp.NET to validate the TOTP code. 
            // bool isValid = new Totp(Base32Encoding.ToBytes(user.MfaSecret)).VerifyTotp(code, out long timeWindowUsed);
            bool isValidCode = code == "123456"; // MOCKED FOR NOW

            return isValidCode ? (true, "MFA setup verified") : (false, "Invalid authenticator code");
        }

        public async Task<(bool Success, string Message)> EnableMfaAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || string.IsNullOrEmpty(user.MfaSecret)) return (false, "MFA setup not initialized");

            // TODO: Use Otp.NET to validate the TOTP code.
            bool isValidCode = code == "123456"; // MOCKED FOR NOW
            if (!isValidCode) return (false, "Invalid authenticator code");

            var recoveryCodes = GenerateRecoveryCodes();
            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, true)
                .Set(u => u.MfaRecoveryCodes, recoveryCodes);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return (true, "MFA enabled successfully");
        }

        public async Task<(bool Success, string Message)> DisableMfaAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");
            if (!user.MfaEnabled) return (false, "MFA is not enabled");

            // TODO: Use Otp.NET to validate the TOTP code OR verify a password
            bool isValidCode = code == "123456"; // MOCKED FOR NOW
            if (!isValidCode) return (false, "Invalid authenticator code");

            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, false)
                .Set(u => u.MfaSecret, null)
                .Set(u => u.MfaRecoveryCodes, new List<string>());

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return (true, "MFA disabled successfully");
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> MfaChallengeAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return (false, "Invalid request or MFA not enabled", string.Empty, string.Empty, null);

            // TODO: Use Otp.NET to validate the TOTP code
            bool isValidCode = code == "123456"; // MOCKED FOR NOW
            if (!isValidCode) return (false, "Invalid authenticator code", string.Empty, string.Empty, null);

            var newSession = CreateSession();
            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.Add(newSession);

            var update = Builders<User>.Update.Set(u => u.Sessions, activeSessions);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            string token = GenerateJwtToken(user);
            return (true, "Login successful", token, newSession.RefreshToken, ToUserDto(user));
        }

        public async Task<List<string>?> GetRecoveryCodesAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return null;
            return user.MfaRecoveryCodes;
        }

        public async Task<(bool Success, string Message, List<string>? Codes)> RegenerateRecoveryCodesAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return (false, "MFA is not enabled", null);

            var newCodes = GenerateRecoveryCodes();
            var update = Builders<User>.Update.Set(u => u.MfaRecoveryCodes, newCodes);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);

            return (true, "Recovery codes regenerated successfully", newCodes);
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> VerifyRecoveryCodeAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return (false, "Invalid request or MFA not enabled", string.Empty, string.Empty, null);

            if (!user.MfaRecoveryCodes.Contains(code))
                return (false, "Invalid recovery code", string.Empty, string.Empty, null);

            // Remove the used recovery code so it can't be used again
            var updatedCodes = user.MfaRecoveryCodes.Where(c => c != code).ToList();

            var newSession = CreateSession();
            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.Add(newSession);

            var update = Builders<User>.Update
                .Set(u => u.MfaRecoveryCodes, updatedCodes)
                .Set(u => u.Sessions, activeSessions);

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            string token = GenerateJwtToken(user);
            return (true, "Login successful using recovery code", token, newSession.RefreshToken, ToUserDto(user));
        }

        // Add this private helper method if you didn't include it in a previous step
        private List<string> GenerateRecoveryCodes(int count = 10)
        {
            var codes = new List<string>();
            for (int i = 0; i < count; i++)
            {
                codes.Add(Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper());
            }
            return codes;
        }

        // ==========================================
        // 7. SESSIONS & DEVICES
        // ==========================================
        public async Task<List<ActiveSession>> GetUserSessionsAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            return user != null ? CleanupExpiredSessions(user.Sessions) : new List<ActiveSession>();
        }

        public async Task<bool> RevokeSessionAsync(string userId, string sessionId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return false;

            var sessions = user.Sessions;
            sessions.RemoveAll(s => s.SessionId == sessionId);

            var update = Builders<User>.Update.Set(u => u.Sessions, sessions);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> RevokeAllOtherSessionsAsync(string userId, string currentSessionId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return false;

            var currentSession = user.Sessions.FirstOrDefault(s => s.SessionId == currentSessionId);
            var sessionsToKeep = currentSession != null ? new List<ActiveSession> { currentSession } : new List<ActiveSession>();

            var update = Builders<User>.Update.Set(u => u.Sessions, sessionsToKeep);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 8. PRIVATE HELPER METHODS
        // ==========================================
        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? "YourSuperSecretKeyForJWTTokenGeneration12345678901234567890";
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "ExamSystem";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "ExamSystemUsers";

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim("role", ((int)user.Role).ToString()),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("userId", user.Id),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private ActiveSession CreateSession()
        {
            return new ActiveSession
            {
                SessionId = Guid.NewGuid().ToString(),
                RefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                Expiry = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };
        }
        public bool ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            try
            {
                var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

                var jwtSettings = _configuration.GetSection("Jwt");
                var key = Encoding.UTF8.GetBytes(
                    jwtSettings["Key"]
                    ?? "YourSuperSecretKeyForJWTTokenGeneration12345678901234567890"
                );

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = jwtSettings["Issuer"],
                    ValidAudience = jwtSettings["Audience"],

                    IssuerSigningKey = new SymmetricSecurityKey(key),

                    ClockSkew = TimeSpan.Zero
                }, out _);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RevokeTokenAsync(string userId, string token)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
                return false;

            var user = await _mongoDb.Users
                .Find(u => u.Id == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null || user.Sessions == null)
                return false;

            var session = user.Sessions
                .FirstOrDefault(s => s.RefreshToken == token);

            if (session == null)
                return false;

            var sessions = user.Sessions
                .Where(s => s.RefreshToken != token)
                .ToList();

            var update = Builders<User>.Update
                .Set(u => u.Sessions, sessions);

            var result = await _mongoDb.Users.UpdateOneAsync(
                u => u.Id == userId,
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<bool> RevokeAllTokensAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var update = Builders<User>.Update
                .Set(u => u.Sessions, new List<ActiveSession>());

            var result = await _mongoDb.Users.UpdateOneAsync(
                u => u.Id == userId && !u.IsDeleted,
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<bool> PatchCurrentUserAsync(string userId, object request)
        {
            if (string.IsNullOrWhiteSpace(userId) || request == null)
                return false;

            var user = await _mongoDb.Users
                .Find(u => u.Id == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null)
                return false;

            var json = System.Text.Json.JsonSerializer.Serialize(request);

            var data = System.Text.Json.JsonSerializer.Deserialize<
                Dictionary<string, System.Text.Json.JsonElement>
            >(json);

            if (data == null || data.Count == 0)
                return false;

            var updates = new List<UpdateDefinition<User>>();

            if (data.TryGetValue("fullName", out var fullName))
            {
                updates.Add(
                    Builders<User>.Update.Set(
                        u => u.FullName,
                        fullName.GetString()
                    )
                );
            }

            if (data.TryGetValue("phone", out var phone))
            {
                updates.Add(
                    Builders<User>.Update.Set(
                        u => u.Phone,
                        phone.GetString()
                    )
                );
            }

            if (updates.Count == 0)
                return false;

            var update = Builders<User>.Update.Combine(updates);

            var result = await _mongoDb.Users.UpdateOneAsync(
                u => u.Id == userId && !u.IsDeleted,
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<object?> GetSecuritySettingsAsync(string userId)
        {
            var user = await _mongoDb.Users
                .Find(u => u.Id == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null)
                return null;

            return new
            {
                mfaEnabled = user.MfaEnabled,
                emailVerified = user.IsEmailVerified,
                isActive = user.IsActive,
                isLocked = user.IsLocked,
                lastLoginAt = user.LastLoginAt
            };
        }

        public async Task<List<object>> GetUserActivityAsync(
            string userId,
            int page = 1,
            int pageSize = 10)
        {
            var user = await _mongoDb.Users
                .Find(u => u.Id == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null)
                return new List<object>();

            var sessions = user.Sessions ?? new List<ActiveSession>();

            return sessions
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    sessionId = s.SessionId,
                    ipAddress = s.IpAddress,
                    deviceInfo = s.DeviceInfo,
                    createdAt = s.CreatedAt,
                    expiry = s.Expiry
                })
                .Cast<object>()
                .ToList();
        }

        public async Task<ActiveSession?> GetSessionByIdAsync(
            string userId,
            string sessionId)
        {
            if (string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(sessionId))
                return null;

            var user = await _mongoDb.Users
                .Find(u => u.Id == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null || user.Sessions == null)
                return null;

            return user.Sessions.FirstOrDefault(
                s => s.SessionId == sessionId
            );
        }


        private List<ActiveSession> CleanupExpiredSessions(List<ActiveSession> sessions)
        {
            if (sessions == null) return new List<ActiveSession>();
            return sessions.Where(s => s.Expiry > DateTime.UtcNow).ToList();
        }

        private async Task SendOtpEmailAsync(string email, string otp, string subject = "Verify Your Account")
        {
            string emailBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px;'>
                    <h2>ExamSolution Security</h2>
                    <p>Your verification code is: <strong style='font-size: 24px; color: #0284c7;'>{otp}</strong></p>
                    <p>This code will expire in 10 minutes.</p>
                </div>";
            await _emailService.SendEmailAsync(email, subject, emailBody);
        }

        private string GenerateOtp() => new Random().Next(100000, 999999).ToString();

        // private List<string> GenerateRecoveryCodes(int count = 10)
        // {
        //     var codes = new List<string>();
        //     for (int i = 0; i < count; i++)
        //     {
        //         codes.Add(Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper());
        //     }
        //     return codes;
        // }

        private async Task HandleFailedLoginAttempt(User user)
        {
            var attempts = user.FailedLoginAttempts + 1;
            var update = Builders<User>.Update.Set(u => u.FailedLoginAttempts, attempts);

            if (attempts >= 5)
            {
                update = update.Set(u => u.IsLocked, true).Set(u => u.LockoutEnd, DateTime.UtcNow.AddMinutes(15));
            }

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);
        }

        public static UserDto ToUserDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Phone = user.Phone,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                ProfilePicture = user.ProfilePicture,
                IsEmailVerified = user.IsEmailVerified,
                MfaEnabled = user.MfaEnabled
            };
        }
    }
}