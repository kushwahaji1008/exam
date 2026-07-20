using MongoDB.Driver;
using AuthService.Models;
using BCrypt.Net;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AuthService.Services
{
    public class AuthenticationService
    {
        private readonly MongoDbService _mongoDb;
        private readonly IConfiguration _configuration;

        public AuthenticationService(MongoDbService mongoDb, IConfiguration configuration)
        {
            _mongoDb = mongoDb;
            _configuration = configuration;
        }

        public async Task<(bool Success, string Message, User? User)> RegisterAsync(RegisterRequest request)
        {
            // Check if user already exists
            var existingUser = await _mongoDb.Users.Find(u => u.Email == request.Email).FirstOrDefaultAsync();
            if (existingUser != null)
            {
                return (false, "User with this email already exists", null);
            }

            // Create new user
            var user = new User
            {
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FullName = request.FullName,
                Phone = request.Phone,
                Role = request.Role,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _mongoDb.Users.InsertOneAsync(user);
            return (true, "User registered successfully", user);
        }

        public async Task<(bool Success, string Message, string Token, User? User)> LoginAsync(LoginRequest request)
        {
            // Find user by email
            var user = await _mongoDb.Users.Find(u => u.Email == request.Email).FirstOrDefaultAsync();
            if (user == null)
            {
                return (false, "Invalid email or password", string.Empty, null);
            }

            // Verify password
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return (false, "Invalid email or password", string.Empty, null);
            }

            if (!user.IsActive)
            {
                return (false, "Account is deactivated", string.Empty, null);
            }

            // Update last login
            var update = Builders<User>.Update.Set(u => u.LastLoginAt, DateTime.UtcNow);
            await _mongoDb.Users.UpdateOneAsync(u => u.Id == user.Id, update);

            // Generate JWT token
            var token = GenerateJwtToken(user);

            return (true, "Login successful", token, user);
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            return await _mongoDb.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _mongoDb.Users.Find(_ => true).ToListAsync();
        }

        public async Task<bool> UpdateUserAsync(string userId, User updatedUser)
        {
            var result = await _mongoDb.Users.ReplaceOneAsync(u => u.Id == userId, updatedUser);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            var result = await _mongoDb.Users.DeleteOneAsync(u => u.Id == userId);
            return result.DeletedCount > 0;
        }

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
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("userId", user.Id),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
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
