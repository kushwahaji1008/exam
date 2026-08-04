using MongoDB.Driver;
using AuthService.Models;
using BCrypt.Net;
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

            string generatedOtp = new Random().Next(100000, 999999).ToString();
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
                    OtpExpiry = expiryTime
                };
                await _mongoDb.Users.InsertOneAsync(user);
            }

            string emailBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px;'>
                    <h2>Welcome to ExamSolution!</h2>
                    <p>Your verification code is: <strong style='font-size: 24px; color: #0284c7;'>{generatedOtp}</strong></p>
                    <p>This code will expire in 10 minutes.</p>
                </div>";

            await _emailService.SendEmailAsync(request.Email, "Verify Your Account", emailBody);
            return (true, "Registration initiated. Please check your email for the OTP.");
        }

        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> VerifyOtpAsync(VerifyOtpRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).FirstOrDefaultAsync();

            if (user == null) return (false, "User not found.", string.Empty, string.Empty, null);
            if (user.IsEmailVerified) return (false, "Account is already verified. Please login.", string.Empty, string.Empty, null);
            if (user.Otp != request.Otp) return (false, "Invalid verification code.", string.Empty, string.Empty, null);
            if (DateTime.UtcNow > user.OtpExpiry) return (false, "Verification code has expired. Please register again.", string.Empty, string.Empty, null);

            string refreshToken = GenerateRefreshToken();
            
            var update = Builders<User>.Update
                .Set(u => u.IsEmailVerified, true)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null)
                .Set(u => u.LastLoginAt, DateTime.UtcNow)
                .Set(u => u.RefreshToken, refreshToken)
                .Set(u => u.RefreshTokenExpiry, DateTime.UtcNow.AddDays(7));

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);
            
            string token = GenerateJwtToken(user);
            return (true, "Verification successful.", token, refreshToken, ToUserDto(user));
        }

        // ==========================================
        // 2. LOGIN & LOGOUT
        // ==========================================
        public async Task<(bool Success, string Message, string Token, string RefreshToken, UserDto? User)> LoginAsync(LoginRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return (false, "Invalid email or password", string.Empty, string.Empty, null);

            if (!user.IsEmailVerified) return (false, "Please verify your email address before logging in.", string.Empty, string.Empty, null);
            if (!user.IsActive) return (false, "Account is deactivated. Contact support.", string.Empty, string.Empty, null);

            string refreshToken = GenerateRefreshToken();

            var update = Builders<User>.Update
                .Set(u => u.LastLoginAt, DateTime.UtcNow)
                .Set(u => u.RefreshToken, refreshToken)
                .Set(u => u.RefreshTokenExpiry, DateTime.UtcNow.AddDays(7));

            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            string token = GenerateJwtToken(user);
            return (true, "Login successful", token, refreshToken, ToUserDto(user));
        }

        public async Task<bool> LogoutAsync(string userId)
        {
            var update = Builders<User>.Update.Set(u => u.RefreshToken, null).Set(u => u.RefreshTokenExpiry, null);
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
            
            if (!isPasswordReset && user.IsEmailVerified) 
                return (false, "Account is already verified.");

            string generatedOtp = new Random().Next(100000, 999999).ToString();
            var update = Builders<User>.Update.Set(u => u.Otp, generatedOtp).Set(u => u.OtpExpiry, DateTime.UtcNow.AddMinutes(10));
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            string subject = isPasswordReset ? "Password Reset Verification" : "Verify Your Account";
            string emailBody = $@"<div style='font-family: Arial, sans-serif; padding: 20px;'>
                                    <h2>ExamSolution Security</h2>
                                    <p>Your one-time passcode is: <strong style='font-size: 24px; color: #0284c7;'>{generatedOtp}</strong></p>
                                    <p>This code will expire in 10 minutes.</p>
                                  </div>";

            await _emailService.SendEmailAsync(email, subject, emailBody);
            return (true, "OTP sent successfully. Please check your email.");
        }

        public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest request)
        {
            var user = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return (false, "User not found.");
            if (user.Otp != request.Otp) return (false, "Invalid verification code.");
            if (DateTime.UtcNow > user.OtpExpiry) return (false, "Verification code has expired.");

            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            var update = Builders<User>.Update.Set(u => u.PasswordHash, hashedPassword).Set(u => u.Otp, null).Set(u => u.OtpExpiry, null);
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
            var user = await _mongoDb.Users.Find(u => u.RefreshToken == request.RefreshToken && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || user.RefreshTokenExpiry <= DateTime.UtcNow)
                return (false, "Invalid or expired refresh token. Please log in again.", string.Empty, string.Empty, null);

            string newJwtToken = GenerateJwtToken(user);
            string newRefreshToken = GenerateRefreshToken();

            var update = Builders<User>.Update.Set(u => u.RefreshToken, newRefreshToken).Set(u => u.RefreshTokenExpiry, DateTime.UtcNow.AddDays(7));
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            return (true, "Token refreshed successfully", newJwtToken, newRefreshToken, ToUserDto(user));
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

        // Secure Partial Update for Profile!
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
            var update = Builders<User>.Update.Set(u => u.IsDeleted, true);
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 5. HELPER METHODS
        // ==========================================
        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? "YourSuperSecretKeyForJWTTokenGeneration12345678901234567890";
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "ExamSystem";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "ExamSystemUsers";

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim("role", ((int)user.Role).ToString()),
                new Claim("userId", user.Id),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2), // JWT expires faster now that we have Refresh Tokens
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateRefreshToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
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
                ProfilePicture = user.ProfilePicture
            };
        }
    }
}