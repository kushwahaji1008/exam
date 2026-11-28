using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthService.Models;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

namespace AuthService.Services
{
    public interface IReferralRewardHandler
    {
        Task<(bool Success, string Message)> CreditReferralPairAsync(
            string referrerUserId,
            string referrerName,
            string referredUserId,
            string referredName,
            string referredEmail,
            string referralCode);
    }

    public class AuthenticationService
    {
        private const int MaxOtpAttempts = 5;
        private const int MaxFailedLoginAttempts = 5;

        private readonly MongoDbService _mongoDb;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly AuditService? _auditService;
        private readonly IReferralRewardHandler? _referralRewardHandler;

        public AuthenticationService(
            MongoDbService mongoDb,
            IConfiguration configuration,
            IEmailService emailService,
            AuditService? auditService = null,
            IReferralRewardHandler? referralRewardHandler = null)
        {
            _mongoDb = mongoDb;
            _configuration = configuration;
            _emailService = emailService;
            _auditService = auditService;
            _referralRewardHandler = referralRewardHandler;
        }

        // ==========================================
        // 1. REGISTRATION & OTP VERIFICATION
        // ==========================================
        public async Task<(bool Success, string Message)> RegisterAsync(RegisterRequest request)
        {
            if (request.Role == UserRole.Admin || request.Role == UserRole.SuperAdmin)
            {
                return (false, "Security Policy: You cannot self-register as an Admin or SuperAdmin.");
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var existingUser = await _mongoDb.Users
                .Find(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (existingUser != null && existingUser.IsEmailVerified)
                return (false, "User with this email is already registered and verified.");

            string generatedOtp = GenerateOtp();
            DateTime expiryTime = DateTime.UtcNow.AddMinutes(10);
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
            bool isApproved = (request.Role == UserRole.Student);

            User? referrer = null;
            if (!string.IsNullOrWhiteSpace(request.ReferralCode))
            {
                var normalizedCode = request.ReferralCode.Trim().ToUpperInvariant();
                referrer = await _mongoDb.Users
                    .Find(u => u.ReferralCode == normalizedCode && !u.IsDeleted && u.IsActive)
                    .FirstOrDefaultAsync();

                if (referrer == null)
                    return (false, "Invalid referral code. Please check the code or leave it blank.");

                if (referrer.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase))
                    return (false, "You cannot use your own referral code.");
            }

            if (existingUser != null && !existingUser.IsEmailVerified)
            {
                var referralCode = string.IsNullOrWhiteSpace(existingUser.ReferralCode)
                    ? await GenerateUniqueReferralCodeAsync()
                    : existingUser.ReferralCode;

                var update = Builders<User>.Update
                    .Set(u => u.Email, normalizedEmail)
                    .Set(u => u.Otp, generatedOtp)
                    .Set(u => u.OtpExpiry, expiryTime)
                    .Set(u => u.OtpAttemptCount, 0)
                    .Set(u => u.PasswordHash, hashedPassword)
                    .Set(u => u.FullName, request.FullName)
                    .Set(u => u.Role, request.Role)
                    .Set(u => u.Phone, request.Phone)
                    .Set(u => u.IsApproved, isApproved)
                    .Set(u => u.ReferralCode, referralCode)
                    .Set(u => u.ReferredByUserId, referrer?.UserId)
                    .Set(u => u.ReferredByCode, referrer?.ReferralCode);

                await _mongoDb.Users.UpdateOneAsync(u => u.UserId == existingUser.UserId, update);
            }
            else
            {
                string uniqueNineDigitId = await GenerateUniqueNineDigitUserIdAsync();
                string referralCode = await GenerateUniqueReferralCodeAsync();

                var user = new User
                {
                    UserId = uniqueNineDigitId,
                    Email = normalizedEmail,
                    UserName = normalizedEmail.Split('@')[0],
                    PasswordHash = hashedPassword,
                    FullName = request.FullName,
                    Phone = request.Phone,
                    Role = request.Role,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    IsEmailVerified = false,
                    IsApproved = isApproved,
                    Otp = generatedOtp,
                    OtpExpiry = expiryTime,
                    OtpAttemptCount = 0,
                    ReferralCode = referralCode,
                    ReferredByUserId = referrer?.UserId,
                    ReferredByCode = referrer?.ReferralCode,
                    Sessions = new List<ActiveSession>()
                };
                await _mongoDb.Users.InsertOneAsync(user);
            }

            await SendOtpEmailAsync(normalizedEmail, generatedOtp);
            if (_auditService != null)
                await _auditService.LogAuditAsync(normalizedEmail, "UserRegistered", $"Registration initiated for role {request.Role}");

            string responseMessage = request.Role == UserRole.Teacher
                ? "Registration initiated. Please check your email for the OTP. Note: Teacher accounts require admin approval before full access."
                : "Registration initiated. Please check your email for the OTP.";

            return (true, responseMessage);
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> VerifyOtpAsync(VerifyOtpRequest request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var user = await _mongoDb.Users
                .Find(u => (u.Email.ToLower() == normalizedEmail || (u.PendingEmail != null && u.PendingEmail.ToLower() == normalizedEmail)) && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null) return (false, "User not found.", string.Empty, string.Empty, null);

            bool isPendingEmailChange = !string.IsNullOrEmpty(user.PendingEmail) &&
                                        user.PendingEmail.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase);

            if (!isPendingEmailChange && user.IsEmailVerified)
                return (false, "Account is already verified. Please login.", string.Empty, string.Empty, null);

            if (string.IsNullOrEmpty(user.Otp) || user.OtpExpiry == null || DateTime.UtcNow > user.OtpExpiry)
                return (false, "Verification code has expired. Please request a new code.", string.Empty, string.Empty, null);

            if (user.OtpAttemptCount >= MaxOtpAttempts)
            {
                await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId,
                    Builders<User>.Update.Set(u => u.Otp, null).Set(u => u.OtpExpiry, null));
                if (_auditService != null)
                    await _auditService.LogSecurityEventAsync(user.UserId, "OtpBruteForceBlocked", "Warning", "OTP invalidated after too many failed attempts.");
                return (false, "Too many invalid verification attempts. Please request a new code.", string.Empty, string.Empty, null);
            }

            if (user.Otp != request.Otp.Trim())
            {
                await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId,
                    Builders<User>.Update.Inc(u => u.OtpAttemptCount, 1));
                return (false, "Invalid verification code.", string.Empty, string.Empty, null);
            }

            var updates = new List<UpdateDefinition<User>>
            {
                Builders<User>.Update.Set(u => u.IsEmailVerified, true),
                Builders<User>.Update.Set(u => u.Otp, null),
                Builders<User>.Update.Set(u => u.OtpExpiry, null),
                Builders<User>.Update.Set(u => u.OtpAttemptCount, 0)
            };

            if (isPendingEmailChange && user.PendingEmail != null)
            {
                user.Email = user.PendingEmail;
                user.UserName = user.PendingEmail.Split('@')[0];
                user.PendingEmail = null;
                updates.Add(Builders<User>.Update.Set(u => u.Email, user.Email));
                updates.Add(Builders<User>.Update.Set(u => u.UserName, user.UserName));
                updates.Add(Builders<User>.Update.Set(u => u.PendingEmail, null));
            }
            user.IsEmailVerified = true;
            user.ReferralCode = await EnsureReferralCodeAsync(user);

            // Enforce approval/active/MFA status BEFORE issuing session or JWT
            if (!user.IsApproved || !user.IsActive || user.MfaEnabled)
            {
                await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, Builders<User>.Update.Combine(updates));
                if (!isPendingEmailChange && user.IsActive)
                    await TryAwardSignupReferralAsync(user);

                if (_auditService != null)
                    await _auditService.LogAuditAsync(user.UserId, "EmailVerified", isPendingEmailChange ? "Pending email verified." : "Email verified; login deferred until approved/active.");

                var msg = !user.IsApproved
                    ? "Email verified successfully. Your account is pending admin approval."
                    : (!user.IsActive ? "Email verified, but account is deactivated." : "Email verified successfully. Please log in with MFA.");
                return (true, msg, string.Empty, string.Empty, ToUserDto(user));
            }

            var newSession = CreateSession();
            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.Add(newSession);

            updates.Add(Builders<User>.Update.Set(u => u.LastLoginAt, DateTime.UtcNow));
            updates.Add(Builders<User>.Update.Set(u => u.Sessions, activeSessions));

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, Builders<User>.Update.Combine(updates));

            if (!isPendingEmailChange)
                await TryAwardSignupReferralAsync(user);

            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "EmailVerified", "Email verified and session created.");

            string token = await GenerateJwtTokenAsync(user);
            return (true, "Verification successful.", token, newSession.RefreshToken, ToUserDto(user));
        }

        // ==========================================
        // 2. LOGIN & LOGOUT
        // ==========================================
        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> LoginAsync(LoginRequest request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var user = await _mongoDb.Users
                .Find(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user != null && user.IsLocked && user.LockoutEnd > DateTime.UtcNow)
            {
                if (_auditService != null)
                    await _auditService.LogSecurityEventAsync(user.UserId, "LockedLoginAttempt", "Warning", "Login attempt while account locked.");
                return (false, $"Account locked until {user.LockoutEnd:u}. Try again later.", string.Empty, string.Empty, null);
            }

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                if (user != null) await HandleFailedLoginAttempt(user);
                return (false, "Invalid email or password", string.Empty, string.Empty, null);
            }

            if (!user.IsEmailVerified) return (false, "Please verify your email address before logging in.", string.Empty, string.Empty, null);
            if (!user.IsActive) return (false, "Account is deactivated. Contact support.", string.Empty, string.Empty, null);
            if (!user.IsApproved)
            {
                return (false, "Your account is pending admin approval. You can login once an admin approves your profile.", string.Empty, string.Empty, null);
            }
            if (user.RequiresPasswordReset)
            {
                return (false, "Password reset is required before logging in. Please reset your password.", string.Empty, string.Empty, null);
            }

            if (user.MfaEnabled)
            {
                return (false, "MFA_REQUIRED", string.Empty, string.Empty, ToUserDto(user));
            }

            user.ReferralCode = await EnsureReferralCodeAsync(user);

            var newSession = CreateSession();
            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.Add(newSession);

            var update = Builders<User>.Update
                .Set(u => u.LastLoginAt, DateTime.UtcNow)
                .Set(u => u.FailedLoginAttempts, 0)
                .Set(u => u.IsLocked, false)
                .Set(u => u.LockoutEnd, null)
                .Set(u => u.Sessions, activeSessions);

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);

            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "UserLoggedIn", $"User logged in (session {newSession.SessionId}).");

            string token = await GenerateJwtTokenAsync(user);
            return (true, "Login successful", token, newSession.RefreshToken, ToUserDto(user));
        }

        public async Task<bool> LogoutAsync(string userId, string? refreshToken = null)
        {
            UpdateResult result;
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var pullUpdate = Builders<User>.Update.PullFilter(u => u.Sessions, s => s.RefreshToken == refreshToken);
                result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, pullUpdate);
            }
            else
            {
                var clearUpdate = Builders<User>.Update.Set(u => u.Sessions, new List<ActiveSession>());
                result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, clearUpdate);
            }

            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, "UserLoggedOut", string.IsNullOrWhiteSpace(refreshToken) ? "All sessions terminated on logout." : "Single session terminated on logout.");

            return result.MatchedCount > 0;
        }

        // ==========================================
        // 3. PASSWORD MANAGEMENT & REFRESH TOKENS
        // ==========================================
        public async Task<(bool Success, string Message)> ResendOtpAsync(string email, bool isPasswordReset = false)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var user = await _mongoDb.Users
                .Find(u => (u.Email.ToLower() == normalizedEmail || (u.PendingEmail != null && u.PendingEmail.ToLower() == normalizedEmail)) && !u.IsDeleted)
                .FirstOrDefaultAsync();
            if (user == null) return (false, "User not found.");

            bool isPendingEmail = !string.IsNullOrEmpty(user.PendingEmail) &&
                                  user.PendingEmail.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase);

            if (!isPasswordReset && !isPendingEmail && user.IsEmailVerified)
                return (false, "Account is already verified.");

            string generatedOtp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Otp, generatedOtp)
                .Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(10))
                .Set(u => u.OtpAttemptCount, 0);

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);

            await SendOtpEmailAsync(normalizedEmail, generatedOtp, isPasswordReset ? "Password Reset Verification" : "Verify Your Account");
            return (true, "OTP sent successfully. Please check your email.");
        }

        public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var user = await _mongoDb.Users
                .Find(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted)
                .FirstOrDefaultAsync();
            if (user == null || string.IsNullOrEmpty(user.Otp) || user.OtpExpiry == null || DateTime.UtcNow > user.OtpExpiry)
                return (false, "Invalid or expired verification code.");

            if (user.OtpAttemptCount >= MaxOtpAttempts)
            {
                await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId,
                    Builders<User>.Update.Set(u => u.Otp, null).Set(u => u.OtpExpiry, null));
                return (false, "Too many invalid attempts. Please request a new reset code.");
            }

            if (user.Otp != request.Otp.Trim())
            {
                await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId,
                    Builders<User>.Update.Inc(u => u.OtpAttemptCount, 1));
                return (false, "Invalid or expired verification code.");
            }

            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, hashedPassword)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null)
                .Set(u => u.OtpAttemptCount, 0)
                .Set(u => u.RequiresPasswordReset, false)
                .Set(u => u.FailedLoginAttempts, 0)
                .Set(u => u.IsLocked, false)
                .Set(u => u.LockoutEnd, null)
                .Set(u => u.Sessions, new List<ActiveSession>());

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);
            if (_auditService != null)
                await _auditService.LogSecurityEventAsync(user.UserId, "PasswordReset", "Info", "Password reset via OTP.");

            return (true, "Password has been reset successfully. You can now log in.");
        }

        public async Task<(bool Success, string Message)> ChangePasswordAsync(string userId, ChangePasswordRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found.");
            if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash)) return (false, "Incorrect current password.");

            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, hashedPassword)
                .Set(u => u.RequiresPasswordReset, false);
            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);

            if (_auditService != null)
                await _auditService.LogSecurityEventAsync(user.UserId, "PasswordChanged", "Info", "User changed account password.");

            return (true, "Password changed successfully.");
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> RefreshTokenAsync(RefreshTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return (false, "Refresh token is required.", string.Empty, string.Empty, null);

            var user = await _mongoDb.Users
                .Find(u => u.Sessions.Any(s => s.RefreshToken == request.RefreshToken) && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null) return (false, "Invalid token.", string.Empty, string.Empty, null);

            // Validate supplied JWT (ignoring lifetime) when provided to bind access+refresh tokens
            if (!string.IsNullOrWhiteSpace(request.Token))
            {
                var tokenUserId = ExtractUserIdFromPossiblyExpiredJwt(request.Token);
                if (string.IsNullOrEmpty(tokenUserId) || tokenUserId != user.UserId)
                    return (false, "Access token does not match session owner.", string.Empty, string.Empty, null);
            }

            if (!user.IsActive || !user.IsApproved || user.RequiresPasswordReset || (user.IsLocked && user.LockoutEnd > DateTime.UtcNow))
                return (false, "Account is not eligible to refresh tokens.", string.Empty, string.Empty, null);

            var existingSession = user.Sessions.FirstOrDefault(s => s.RefreshToken == request.RefreshToken);
            if (existingSession == null || existingSession.Expiry <= DateTime.UtcNow)
                return (false, "Refresh token expired. Please log in again.", string.Empty, string.Empty, null);

            var newSession = CreateSession();
            newSession.DeviceInfo = existingSession.DeviceInfo;
            newSession.IpAddress = existingSession.IpAddress;

            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.RemoveAll(s => s.RefreshToken == request.RefreshToken);
            activeSessions.Add(newSession);

            // Concurrency guard: ensure original refreshToken is still present during update
            var filter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.UserId, user.UserId),
                Builders<User>.Filter.Eq(u => u.IsDeleted, false),
                Builders<User>.Filter.ElemMatch(u => u.Sessions, s => s.RefreshToken == request.RefreshToken)
            );
            var update = Builders<User>.Update.Set(u => u.Sessions, activeSessions);
            var result = await _mongoDb.Users.UpdateOneAsync(filter, update);
            if (result.ModifiedCount == 0)
                return (false, "Refresh token has already been used or revoked.", string.Empty, string.Empty, null);

            string newJwtToken = await GenerateJwtTokenAsync(user);
            return (true, "Token refreshed successfully", newJwtToken, newSession.RefreshToken, ToUserDto(user));
        }

        // ==========================================
        // 4. USER PROFILE MANAGEMENT
        // ==========================================
        public async Task<UserDto?> GetUserByIdAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return null;
            user.ReferralCode = await EnsureReferralCodeAsync(user);
            return ToUserDto(user);
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

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, "ProfileUpdated", "User updated profile fields.");

            return result.MatchedCount > 0;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.IsDeleted, true)
                .Set(u => u.IsActive, false)
                .Set(u => u.Sessions, new List<ActiveSession>());
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);

            if (result.ModifiedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, "AccountDeleted", "User deleted own account.");

            return result.ModifiedCount > 0;
        }

        public async Task<bool> CheckEmailExistsAsync(string email)
        {
            var normalized = email.Trim().ToLowerInvariant();
            return await _mongoDb.Users.Find(u => u.Email.ToLower() == normalized && !u.IsDeleted).AnyAsync();
        }

        public async Task<bool> CheckUsernameExistsAsync(string username)
        {
            var normalized = username.Trim().ToLowerInvariant();
            return await _mongoDb.Users.Find(u => u.UserName.ToLower() == normalized && !u.IsDeleted).AnyAsync();
        }

        // ==========================================
        // 5. EMAIL & PHONE MANAGEMENT
        // ==========================================
        public async Task<(bool Success, string Message)> ChangeEmailAsync(string userId, ChangeEmailRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)) return (false, "Incorrect password");

            var newEmail = request.NewEmail.Trim().ToLowerInvariant();
            var emailExists = await CheckEmailExistsAsync(newEmail);
            if (emailExists) return (false, "Email is already in use by another account");

            string generatedOtp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.PendingEmail, newEmail)
                .Set(u => u.Otp, generatedOtp)
                .Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(15))
                .Set(u => u.OtpAttemptCount, 0);

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);
            await SendOtpEmailAsync(newEmail, generatedOtp, "Verify Your New Email");

            if (_auditService != null)
                await _auditService.LogSecurityEventAsync(user.UserId, "EmailChangeInitiated", "Info", $"Verification sent for pending email {newEmail}.");

            return (true, "Verification code sent to your new email. Verify it to complete the email change.");
        }

        public async Task<(bool Success, string Message)> AddOrChangePhoneAsync(string userId, AddPhoneRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");

            string generatedOtp = GenerateOtp();
            var update = Builders<User>.Update
                .Set(u => u.Phone, request.PhoneNumber)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.PhoneOtp, generatedOtp)
                .Set(u => u.PhoneOtpExpiry, DateTime.UtcNow.AddMinutes(10))
                .Set(u => u.PhoneOtpAttemptCount, 0);

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            return result.MatchedCount > 0 ? (true, "OTP sent to phone") : (false, "Failed to update phone");
        }

        public async Task<(bool Success, string Message)> VerifyPhoneAsync(string userId, string otp)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || user.PhoneOtp != otp || user.PhoneOtpExpiry == null || DateTime.UtcNow > user.PhoneOtpExpiry)
                return (false, "Invalid or expired OTP");

            var update = Builders<User>.Update
                .Set(u => u.IsPhoneVerified, true)
                .Set(u => u.PhoneOtp, null)
                .Set(u => u.PhoneOtpExpiry, null)
                .Set(u => u.PhoneOtpAttemptCount, 0);
            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId, update);

            return (true, "Phone verified successfully");
        }

        public async Task<bool> RemovePhoneAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.Phone, null)
                .Set(u => u.IsPhoneVerified, false)
                .Set(u => u.PhoneOtp, null)
                .Set(u => u.PhoneOtpExpiry, null);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            return result.MatchedCount > 0;
        }

        // ==========================================
        // 6. MULTI-FACTOR AUTHENTICATION (MFA - Real RFC 6238 TOTP)
        // ==========================================
        public Task<(bool Success, string Message, string? Secret, string? QrCodeUrl)> SetupMfaAsync(string userId) =>
            SetupMfaAsync(userId, "Authenticator");

        public Task<(bool Success, string Message)> VerifyAndEnableMfaAsync(string userId, string code) =>
            EnableMfaAsync(userId, code);

        public async Task<(bool Success, string Message)> DisableMfaAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, false)
                .Set(u => u.MfaSecret, null)
                .Set(u => u.MfaRecoveryCodes, new List<string>());

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "MfaDisabled", "Warning", "MFA disabled on account.");
            return result.MatchedCount > 0 ? (true, "MFA disabled") : (false, "Failed to disable MFA");
        }

        public async Task<object?> GetMfaInfoAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return null;

            return new
            {
                enabled = user.MfaEnabled,
                methods = user.MfaEnabled ? new[] { "Authenticator" } : Array.Empty<string>()
            };
        }

        public async Task<bool> GetMfaStatusAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            return user?.MfaEnabled ?? false;
        }

        public async Task<(bool Success, string Message, string? Secret, string? QrCodeUrl)> SetupMfaAsync(string userId, string method)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found", null, null);
            if (user.MfaEnabled) return (false, "MFA is already enabled", null, null);

            string secret = TotpHelper.GenerateBase32Secret(20);
            string qrCodeUrl = $"otpauth://totp/ExamApp:{Uri.EscapeDataString(user.Email)}?secret={secret}&issuer=ExamApp&digits=6&period=30";

            var update = Builders<User>.Update.Set(u => u.MfaSecret, secret);
            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId, update);

            return (true, "MFA setup initialized", secret, qrCodeUrl);
        }

        public async Task<(bool Success, string Message)> VerifyMfaSetupAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || string.IsNullOrEmpty(user.MfaSecret)) return (false, "MFA setup not initialized");

            bool isValidCode = TotpHelper.VerifyCode(user.MfaSecret, code);
            return isValidCode ? (true, "MFA setup verified") : (false, "Invalid authenticator code");
        }

        public async Task<(bool Success, string Message)> EnableMfaAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || string.IsNullOrEmpty(user.MfaSecret)) return (false, "MFA setup not initialized");

            bool isValidCode = TotpHelper.VerifyCode(user.MfaSecret, code);
            if (!isValidCode) return (false, "Invalid authenticator code");

            var recoveryCodes = GenerateRecoveryCodes();
            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, true)
                .Set(u => u.MfaRecoveryCodes, recoveryCodes);

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId, update);
            if (_auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "MfaEnabled", "Info", "TOTP MFA enabled.");

            return (true, "MFA enabled successfully");
        }

        public async Task<(bool Success, string Message)> DisableMfaAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found");
            if (!user.MfaEnabled) return (false, "MFA is not enabled");

            bool isValidCode = TotpHelper.VerifyCode(user.MfaSecret, code) ||
                               (!string.IsNullOrWhiteSpace(code) && user.MfaRecoveryCodes.Contains(code.Trim().ToUpperInvariant()));
            if (!isValidCode) return (false, "Invalid authenticator code");

            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, false)
                .Set(u => u.MfaSecret, null)
                .Set(u => u.MfaRecoveryCodes, new List<string>());

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId, update);
            if (_auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "MfaDisabled", "Warning", "User disabled MFA.");

            return (true, "MFA disabled successfully");
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> MfaChallengeAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return (false, "Invalid request or MFA not enabled", string.Empty, string.Empty, null);
            if (!user.IsActive || !user.IsApproved || user.RequiresPasswordReset)
                return (false, "Account is not active or approved for login.", string.Empty, string.Empty, null);
            if (user.IsLocked && user.LockoutEnd > DateTime.UtcNow)
                return (false, $"Account locked until {user.LockoutEnd:u}. Try again later.", string.Empty, string.Empty, null);

            if (!TotpHelper.VerifyCode(user.MfaSecret, code))
            {
                await HandleFailedLoginAttempt(user, "MfaFailed");
                return (false, "Invalid authenticator code", string.Empty, string.Empty, null);
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
            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);

            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "MfaChallengePassed", "User completed MFA login.");

            string token = await GenerateJwtTokenAsync(user);
            return (true, "Login successful", token, newSession.RefreshToken, ToUserDto(user));
        }

        public async Task<List<string>?> GetRecoveryCodesAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return null;
            return user.MfaRecoveryCodes;
        }

        public async Task<(bool Success, string Message, List<string>? Codes)> RegenerateRecoveryCodesAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return (false, "MFA is not enabled", null);

            var newCodes = GenerateRecoveryCodes();
            var update = Builders<User>.Update.Set(u => u.MfaRecoveryCodes, newCodes);
            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId, update);

            if (_auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "MfaRecoveryCodesRegenerated", "Info", "User regenerated recovery codes.");

            return (true, "Recovery codes regenerated successfully", newCodes);
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> VerifyRecoveryCodeAsync(string userId, string code)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !user.MfaEnabled) return (false, "Invalid request or MFA not enabled", string.Empty, string.Empty, null);
            if (!user.IsActive || !user.IsApproved || user.RequiresPasswordReset)
                return (false, "Account is not active or approved for login.", string.Empty, string.Empty, null);
            if (user.IsLocked && user.LockoutEnd > DateTime.UtcNow)
                return (false, $"Account locked until {user.LockoutEnd:u}. Try again later.", string.Empty, string.Empty, null);

            var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalizedCode) || !user.MfaRecoveryCodes.Contains(normalizedCode))
            {
                await HandleFailedLoginAttempt(user, "MfaRecoveryCodeFailed");
                return (false, "Invalid recovery code", string.Empty, string.Empty, null);
            }

            var updatedCodes = user.MfaRecoveryCodes.Where(c => c != normalizedCode).ToList();
            var newSession = CreateSession();
            var activeSessions = CleanupExpiredSessions(user.Sessions);
            activeSessions.Add(newSession);

            var update = Builders<User>.Update
                .Set(u => u.LastLoginAt, DateTime.UtcNow)
                .Set(u => u.FailedLoginAttempts, 0)
                .Set(u => u.IsLocked, false)
                .Set(u => u.LockoutEnd, null)
                .Set(u => u.MfaRecoveryCodes, updatedCodes)
                .Set(u => u.Sessions, activeSessions);

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);

            if (_auditService != null)
                await _auditService.LogSecurityEventAsync(user.UserId, "MfaRecoveryCodeUsed", "Warning", $"Recovery code consumed; {updatedCodes.Count} remaining.");

            string token = await GenerateJwtTokenAsync(user);
            return (true, "Login successful using recovery code", token, newSession.RefreshToken, ToUserDto(user));
        }

        private List<string> GenerateRecoveryCodes(int count = 10)
        {
            var codes = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                codes.Add(Convert.ToHexString(RandomNumberGenerator.GetBytes(4)));
            }
            return codes;
        }

        // ==========================================
        // 7. SESSIONS & DEVICES
        // ==========================================
        public async Task<List<ActiveSession>> GetUserSessionsAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            return user != null ? CleanupExpiredSessions(user.Sessions) : new List<ActiveSession>();
        }

        public async Task<bool> RevokeSessionAsync(string userId, string sessionId)
        {
            var update = Builders<User>.Update.PullFilter(u => u.Sessions, s => s.SessionId == sessionId);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> RevokeAllOtherSessionsAsync(string userId, string currentSessionId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return false;

            var currentSession = user.Sessions.FirstOrDefault(s => s.SessionId == currentSessionId);
            var sessionsToKeep = currentSession != null ? new List<ActiveSession> { currentSession } : new List<ActiveSession>();

            var update = Builders<User>.Update.Set(u => u.Sessions, sessionsToKeep);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId, update);
            return result.MatchedCount > 0;
        }

        // ==========================================
        // 8. PRIVATE HELPER METHODS
        // ==========================================
        private string GetRequiredJwtKey()
        {
            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.StartsWith("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Jwt:Key is not configured.");
            return jwtKey;
        }

        private async Task<string> GenerateJwtTokenAsync(User user)
        {
            var jwtKey = GetRequiredJwtKey();
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "ExamSystem";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "ExamSystemUsers";

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim("role", ((int)user.Role).ToString()),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("userId", user.UserId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            // Add dynamic permission claims from AssignedRoleIds + direct Permissions
            var permissionNames = new HashSet<string>(user.Permissions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (user.AssignedRoleIds != null && user.AssignedRoleIds.Count > 0)
            {
                var roles = await _mongoDb.Roles.Find(r => user.AssignedRoleIds.Contains(r.Id)).ToListAsync();
                foreach (var role in roles)
                {
                    if (!string.IsNullOrWhiteSpace(role.Name))
                        claims.Add(new Claim(ClaimTypes.Role, role.Name));
                }

                var permIds = roles.SelectMany(r => r.PermissionIds).Distinct().ToList();
                if (permIds.Count > 0)
                {
                    var perms = await _mongoDb.Permissions.Find(p => permIds.Contains(p.Id)).ToListAsync();
                    foreach (var perm in perms)
                    {
                        if (!string.IsNullOrWhiteSpace(perm.Name))
                            permissionNames.Add(perm.Name);
                    }
                }
            }

            foreach (var perm in permissionNames)
            {
                claims.Add(new Claim("permission", perm));
            }

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string? ExtractUserIdFromPossiblyExpiredJwt(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtSettings = _configuration.GetSection("Jwt");
                var key = Encoding.UTF8.GetBytes(GetRequiredJwtKey());

                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = false, // intentionally allow expired access token during refresh
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings["Issuer"] ?? "ExamSystem",
                    ValidAudience = jwtSettings["Audience"] ?? "ExamSystemUsers",
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                }, out var validatedToken);

                if (validatedToken is not JwtSecurityToken jwt ||
                    !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return principal.FindFirst("userId")?.Value
                       ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }
            catch
            {
                return null;
            }
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
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtSettings = _configuration.GetSection("Jwt");
                var key = Encoding.UTF8.GetBytes(GetRequiredJwtKey());

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings["Issuer"] ?? "ExamSystem",
                    ValidAudience = jwtSettings["Audience"] ?? "ExamSystemUsers",
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

            var update = Builders<User>.Update.PullFilter(u => u.Sessions, s => s.RefreshToken == token);
            var result = await _mongoDb.Users.UpdateOneAsync(
                u => u.UserId == userId && !u.IsDeleted && u.Sessions.Any(s => s.RefreshToken == token),
                update
            );

            return result.ModifiedCount > 0;
        }

        public async Task<bool> RevokeAllTokensAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var update = Builders<User>.Update.Set(u => u.Sessions, new List<ActiveSession>());
            var result = await _mongoDb.Users.UpdateOneAsync(
                u => u.UserId == userId && !u.IsDeleted,
                update
            );

            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "AllTokensRevoked", "Info", "User revoked all active sessions.");

            return result.MatchedCount > 0;
        }

        public async Task<bool> PatchCurrentUserAsync(string userId, object request)
        {
            if (string.IsNullOrWhiteSpace(userId) || request == null)
                return false;

            var user = await _mongoDb.Users
                .Find(u => u.UserId == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null)
                return false;

            try
            {
                var json = JsonSerializer.Serialize(request);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (data == null || data.Count == 0)
                    return false;

                var updates = new List<UpdateDefinition<User>>();
                foreach (var kvp in data)
                {
                    switch (kvp.Key.ToLowerInvariant())
                    {
                        case "fullname" when kvp.Value.ValueKind == JsonValueKind.String:
                            var fn = kvp.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(fn))
                                updates.Add(Builders<User>.Update.Set(u => u.FullName, fn));
                            break;
                        case "phone":
                            updates.Add(Builders<User>.Update.Set(u => u.Phone, kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.GetString()));
                            break;
                        case "gender" when kvp.Value.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<User>.Update.Set(u => u.Gender, kvp.Value.GetString() ?? string.Empty));
                            break;
                        case "dateofbirth":
                            if (kvp.Value.ValueKind == JsonValueKind.Null)
                                updates.Add(Builders<User>.Update.Set(u => u.DateOfBirth, null));
                            else if (kvp.Value.ValueKind == JsonValueKind.String && kvp.Value.TryGetDateTime(out var dob))
                                updates.Add(Builders<User>.Update.Set(u => u.DateOfBirth, dob));
                            break;
                        case "profilepicture":
                            updates.Add(Builders<User>.Update.Set(u => u.ProfilePicture, kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.GetString()));
                            break;
                    }
                }

                if (updates.Count == 0)
                    return false;

                var result = await _mongoDb.Users.UpdateOneAsync(
                    u => u.UserId == userId && !u.IsDeleted,
                    Builders<User>.Update.Combine(updates)
                );

                return result.MatchedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<object?> GetSecuritySettingsAsync(string userId)
        {
            var user = await _mongoDb.Users
                .Find(u => u.UserId == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null)
                return null;

            return new
            {
                mfaEnabled = user.MfaEnabled,
                emailVerified = user.IsEmailVerified,
                phoneVerified = user.IsPhoneVerified,
                isActive = user.IsActive,
                isLocked = user.IsLocked,
                lastLoginAt = user.LastLoginAt
            };
        }

        public async Task<List<object>> GetUserActivityAsync(string userId, int page = 1, int pageSize = 10)
        {
            var safePage = Math.Max(1, page);
            var safePageSize = Math.Clamp(pageSize, 1, 100);

            var logs = await _mongoDb.AuditLogs
                .Find(a => a.UserId == userId)
                .SortByDescending(a => a.Timestamp)
                .Skip((safePage - 1) * safePageSize)
                .Limit(safePageSize)
                .ToListAsync();

            if (logs.Count > 0)
            {
                return logs.Select(l => (object)new
                {
                    id = l.Id,
                    action = l.ActionType,
                    details = l.Details,
                    ipAddress = l.IpAddress,
                    userAgent = l.UserAgent,
                    timestamp = l.Timestamp
                }).ToList();
            }

            var user = await _mongoDb.Users
                .Find(u => u.UserId == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null)
                return new List<object>();

            return (user.Sessions ?? new List<ActiveSession>())
                .OrderByDescending(s => s.CreatedAt)
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .Select(s => (object)new
                {
                    sessionId = s.SessionId,
                    ipAddress = s.IpAddress,
                    deviceInfo = s.DeviceInfo,
                    createdAt = s.CreatedAt,
                    expiry = s.Expiry
                })
                .ToList();
        }

        public async Task<ActiveSession?> GetSessionByIdAsync(string userId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
                return null;

            var user = await _mongoDb.Users
                .Find(u => u.UserId == userId && !u.IsDeleted)
                .FirstOrDefaultAsync();

            if (user == null || user.Sessions == null)
                return null;

            return user.Sessions.FirstOrDefault(s => s.SessionId == sessionId);
        }

        private async Task<string> GenerateUniqueNineDigitUserIdAsync()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var candidate = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();
                var exists = await _mongoDb.Users.Find(u => u.UserId == candidate).AnyAsync();
                if (!exists) return candidate;
            }
            throw new InvalidOperationException("Unable to allocate unique 9-digit UserId.");
        }

        private async Task<string> GenerateUniqueReferralCodeAsync()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[9];
            chars[0] = 'E';
            chars[1] = 'X';
            chars[2] = 'M';

            for (int attempt = 0; attempt < 25; attempt++)
            {
                for (int i = 3; i < chars.Length; i++)
                {
                    chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
                }

                var code = new string(chars);
                var exists = await _mongoDb.Users.Find(u => u.ReferralCode == code).AnyAsync();
                if (!exists) return code;
            }

            return $"EXM{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }

        public async Task<string> EnsureReferralCodeAsync(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.ReferralCode))
                return user.ReferralCode;

            var generated = await GenerateUniqueReferralCodeAsync();
            var filter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.UserId, user.UserId),
                Builders<User>.Filter.Or(
                    Builders<User>.Filter.Eq(u => u.ReferralCode, null),
                    Builders<User>.Filter.Eq(u => u.ReferralCode, string.Empty)
                )
            );
            var update = Builders<User>.Update.Set(u => u.ReferralCode, generated);
            var updated = await _mongoDb.Users.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });

            if (updated != null && !string.IsNullOrWhiteSpace(updated.ReferralCode))
                return updated.ReferralCode;

            var latest = await _mongoDb.Users.Find(u => u.UserId == user.UserId).FirstOrDefaultAsync();
            return !string.IsNullOrWhiteSpace(latest?.ReferralCode) ? latest.ReferralCode! : generated;
        }

        public async Task<string> EnsureUserReferralCodeAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return string.Empty;
            return await EnsureReferralCodeAsync(user);
        }

        public async Task<(string ReferralCode, bool CanApplyReferralCode, string? ReferredByCode, bool ReferralRewarded)> GetUserReferralStatusAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null)
                return (string.Empty, false, null, false);

            var code = await EnsureReferralCodeAsync(user);
            bool canApply = !user.ReferralRewarded && string.IsNullOrWhiteSpace(user.ReferredByUserId);
            return (code, canApply, user.ReferredByCode, user.ReferralRewarded);
        }

        public async Task<(bool Success, string Message, string? AppliedCode)> ApplyReferralCodeAsync(string currentUserId, string referralCode)
        {
            var normalizedCode = (referralCode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedCode))
                return (false, "Referral code is required.", null);

            var user = await _mongoDb.Users.Find(u => u.UserId == currentUserId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User account not found.", null);
            if (!user.IsActive) return (false, "Account is deactivated.", null);
            if (!user.IsEmailVerified) return (false, "Please verify your email before applying a referral code.", null);
            if (user.ReferralRewarded || !string.IsNullOrWhiteSpace(user.ReferredByUserId))
                return (false, "A referral code has already been applied to your account.", null);

            var referrer = await _mongoDb.Users
                .Find(u => u.ReferralCode == normalizedCode && !u.IsDeleted && u.IsActive)
                .FirstOrDefaultAsync();
            if (referrer == null)
                return (false, "Invalid referral code. Please check and try again.", null);

            if (referrer.UserId == user.UserId)
                return (false, "You cannot apply your own referral code.", null);

            if (string.Equals(referrer.ReferredByUserId, user.UserId, StringComparison.Ordinal))
                return (false, "Circular referral between two accounts is not permitted.", null);

            var claimFilter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.UserId, user.UserId),
                Builders<User>.Filter.Eq(u => u.ReferralRewarded, false),
                Builders<User>.Filter.Or(
                    Builders<User>.Filter.Eq(u => u.ReferredByUserId, null),
                    Builders<User>.Filter.Eq(u => u.ReferredByUserId, string.Empty)
                )
            );
            var claimUpdate = Builders<User>.Update
                .Set(u => u.ReferredByUserId, referrer.UserId)
                .Set(u => u.ReferredByCode, referrer.ReferralCode)
                .Set(u => u.ReferralRewarded, true)
                .Set(u => u.ReferralRewardedAt, DateTime.UtcNow);

            var claimed = await _mongoDb.Users.FindOneAndUpdateAsync(claimFilter, claimUpdate);
            if (claimed == null)
                return (false, "A referral code has already been applied to your account.", null);

            if (_referralRewardHandler != null)
            {
                var (ok, msg) = await _referralRewardHandler.CreditReferralPairAsync(
                    referrer.UserId,
                    referrer.FullName,
                    user.UserId,
                    user.FullName,
                    user.Email,
                    referrer.ReferralCode ?? normalizedCode);

                if (!ok)
                {
                    await _mongoDb.Users.UpdateOneAsync(
                        u => u.UserId == user.UserId,
                        Builders<User>.Update
                            .Set(u => u.ReferredByUserId, null)
                            .Set(u => u.ReferredByCode, null)
                            .Set(u => u.ReferralRewarded, false)
                            .Set(u => u.ReferralRewardedAt, null));
                    return (false, msg, null);
                }
            }

            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "ReferralApplied", $"Applied referral code {referrer.ReferralCode} from user {referrer.UserId}.");

            return (true, "Referral code applied! 100 coins have been added to both your wallet and your friend's wallet.", referrer.ReferralCode);
        }

        private async Task TryAwardSignupReferralAsync(User user)
        {
            if (_referralRewardHandler == null ||
                string.IsNullOrWhiteSpace(user.ReferredByUserId) ||
                user.ReferralRewarded ||
                user.ReferredByUserId == user.UserId)
            {
                return;
            }

            var referrer = await _mongoDb.Users
                .Find(u => u.UserId == user.ReferredByUserId && !u.IsDeleted && u.IsActive)
                .FirstOrDefaultAsync();
            if (referrer == null || referrer.ReferredByUserId == user.UserId)
                return;

            var claimFilter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.UserId, user.UserId),
                Builders<User>.Filter.Eq(u => u.IsEmailVerified, true),
                Builders<User>.Filter.Eq(u => u.ReferralRewarded, false)
            );
            var claimUpdate = Builders<User>.Update
                .Set(u => u.ReferralRewarded, true)
                .Set(u => u.ReferralRewardedAt, DateTime.UtcNow);

            var claimed = await _mongoDb.Users.FindOneAndUpdateAsync(claimFilter, claimUpdate);
            if (claimed == null) return;

            var (ok, _) = await _referralRewardHandler.CreditReferralPairAsync(
                referrer.UserId,
                referrer.FullName,
                user.UserId,
                user.FullName,
                user.Email,
                user.ReferredByCode ?? referrer.ReferralCode ?? string.Empty);

            if (!ok)
            {
                await _mongoDb.Users.UpdateOneAsync(
                    u => u.UserId == user.UserId,
                    Builders<User>.Update
                        .Set(u => u.ReferralRewarded, false)
                        .Set(u => u.ReferralRewardedAt, null));
                return;
            }

            user.ReferralRewarded = true;
            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "ReferralRewardGranted", $"Dual 100-coin referral bonus granted with referrer {referrer.UserId}.");
        }

        private List<ActiveSession> CleanupExpiredSessions(List<ActiveSession>? sessions)
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

        private static string GenerateOtp() =>
            RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString("D6");

        private async Task HandleFailedLoginAttempt(User user, string eventType = "FailedLogin")
        {
            var attempts = user.FailedLoginAttempts + 1;
            var update = Builders<User>.Update.Set(u => u.FailedLoginAttempts, attempts);

            if (attempts >= MaxFailedLoginAttempts)
            {
                update = update.Set(u => u.IsLocked, true).Set(u => u.LockoutEnd, DateTime.UtcNow.AddMinutes(15));
                if (_auditService != null)
                    await _auditService.LogSecurityEventAsync(user.UserId, "AccountLocked", "Critical", $"Account locked after {attempts} failed attempts ({eventType}).");
            }
            else if (_auditService != null)
            {
                await _auditService.LogSecurityEventAsync(user.UserId, eventType, "Warning", $"Failed authentication attempt {attempts}/{MaxFailedLoginAttempts}.");
            }

            await _mongoDb.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);
        }

        public static UserDto ToUserDto(User user)
        {
            return new UserDto
            {
                UserId = user.UserId,
                Email = user.Email,
                UserName = user.UserName,
                FullName = user.FullName,
                Phone = user.Phone,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                ProfilePicture = user.ProfilePicture,
                IsEmailVerified = user.IsEmailVerified,
                MfaEnabled = user.MfaEnabled,
                ReferralCode = user.ReferralCode,
                ReferredByCode = user.ReferredByCode,
                ReferralRewarded = user.ReferralRewarded
            };
        }
    }
}
