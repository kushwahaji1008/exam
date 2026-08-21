using MongoDB.Driver;
using MongoDB.Bson;
using AuthService.Models;
using System.Text.Json;

namespace AuthService.Services
{
    public class UserService
    {
        private readonly MongoDbService _mongoDb;

        public UserService(MongoDbService mongoDb)
        {
            _mongoDb = mongoDb;
        }

        // ==========================================
        // 1. CORE CRUD OPERATIONS
        // ==========================================
        
        public async Task<object> GetAllUsersAsync(UserFilterRequest filter)
        {
            var builder = Builders<User>.Filter;
            var mongoFilter = builder.Eq(u => u.IsDeleted, false);

            if (!string.IsNullOrEmpty(filter.SearchQuery))
            {
                var searchFilter = builder.Or(
                    builder.Regex(u => u.Email, new BsonRegularExpression(filter.SearchQuery, "i")),
                    builder.Regex(u => u.FullName, new BsonRegularExpression(filter.SearchQuery, "i"))
                );
                mongoFilter &= searchFilter;
            }

            if (filter.Role.HasValue)
            {
                mongoFilter &= builder.Eq(u => u.Role, filter.Role.Value);
            }

            if (filter.IsActive.HasValue)
            {
                mongoFilter &= builder.Eq(u => u.IsActive, filter.IsActive.Value);
            }

            var totalRecords = await _mongoDb.Users.CountDocumentsAsync(mongoFilter);
            
            var users = await _mongoDb.Users.Find(mongoFilter)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Limit(filter.PageSize)
                .SortByDescending(u => u.CreatedAt)
                .ToListAsync();

            // Return paginated response
            return new
            {
                TotalRecords = totalRecords,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalPages = (int)Math.Ceiling(totalRecords / (double)filter.PageSize),
                Data = users.Select(AuthenticationService.ToUserDto).ToList()
            };
        }

        public async Task<(bool Success, string Message, string? UserId)> CreateUserAsync(CreateUserAdminRequest request)
        {
            var exists = await _mongoDb.Users.Find(u => u.Email == request.Email && !u.IsDeleted).AnyAsync();
            if (exists) return (false, "User with this email already exists.", null);

            var user = new User
            {
                Email = request.Email,
                UserName = request.Email.Split('@')[0],
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FullName = request.FullName,
                Role = request.Role,
                IsActive = request.IsActive,
                IsEmailVerified = request.IsEmailVerified,
                CreatedAt = DateTime.UtcNow,
                Sessions = new List<ActiveSession>()
            };

            await _mongoDb.Users.InsertOneAsync(user);
            return (true, "User created successfully.", user.Id);
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            // Returning the full User object for Admin view. 
            // In a production app, you might map this to a detailed AdminUserDto to hide PasswordHash.
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user != null) 
            {
                user.PasswordHash = "HIDDEN"; // Sanitize before returning to controller
            }
            return user;
        }

        public async Task<bool> UpdateUserAsync(string userId, UpdateUserAdminRequest request)
        {
            var update = Builders<User>.Update
                .Set(u => u.FullName, request.FullName)
                .Set(u => u.Role, request.Role)
                .Set(u => u.IsActive, request.IsActive)
                .Set(u => u.IsEmailVerified, request.IsEmailVerified);

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> PatchUserAsync(string userId, object request)
        {
            // Note: True JSON Patch requires Microsoft.AspNetCore.JsonPatch.
            // This is a simplified approach assuming a JSON object dictionary payload.
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(request);
                var patchDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (patchDict == null || !patchDict.Any()) return false;

                var updateBuilder = Builders<User>.Update;
                var updates = new List<UpdateDefinition<User>>();

                foreach (var kvp in patchDict)
                {
                    // Prevent modifying critical system fields
                    if (kvp.Key.Equals("Id", StringComparison.OrdinalIgnoreCase) || 
                        kvp.Key.Equals("PasswordHash", StringComparison.OrdinalIgnoreCase)) 
                        continue;

                    updates.Add(updateBuilder.Set(kvp.Key, kvp.Value));
                }

                if (updates.Any())
                {
                    var combinedUpdate = updateBuilder.Combine(updates);
                    var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, combinedUpdate);
                    return result.ModifiedCount > 0;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            // Soft delete
            var update = Builders<User>.Update
                .Set(u => u.IsDeleted, true)
                .Set(u => u.IsActive, false)
                .Set(u => u.Sessions, new List<ActiveSession>()); // Kill active sessions

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 2. ACCOUNT STATUS MANAGEMENT
        // ==========================================

        public async Task<bool> SetUserActiveStatusAsync(string userId, bool isActive)
        {
            var update = Builders<User>.Update.Set(u => u.IsActive, isActive);
            
            // If deactivating, also kill all active sessions
            if (!isActive)
            {
                update = update.Set(u => u.Sessions, new List<ActiveSession>());
            }

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> SetUserLockStatusAsync(string userId, bool isLocked)
        {
            var update = Builders<User>.Update
                .Set(u => u.IsLocked, isLocked)
                .Set(u => u.FailedLoginAttempts, 0); // Reset attempts on unlock/lock

            if (isLocked)
            {
                // Lock until the year 9999 (indefinite admin lock)
                update = update.Set(u => u.LockoutEnd, DateTime.UtcNow.AddYears(100))
                               .Set(u => u.Sessions, new List<ActiveSession>());
            }
            else
            {
                update = update.Set(u => u.LockoutEnd, null);
            }

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 3. SECURITY & VERIFICATION
        // ==========================================

        public async Task<bool> ForcePasswordResetAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.RequiresPasswordReset, true)
                .Set(u => u.Sessions, new List<ActiveSession>()); // Log them out so they are forced to reset

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> VerifyUserEmailManuallyAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.IsEmailVerified, true)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null);

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ResetUserMfaAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, false)
                .Set(u => u.MfaSecret, null)
                .Set(u => u.MfaRecoveryCodes, new List<string>());

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> RevokeAllUserSessionsAsync(string userId)
        {
            var update = Builders<User>.Update.Set(u => u.Sessions, new List<ActiveSession>());
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.Id == userId && !u.IsDeleted, update);
            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 4. AUDIT & ACTIVITY
        // ==========================================

        public async Task<List<ActiveSession>?> GetUserSessionsAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return null;

            // Return only sessions that have not expired
            return user.Sessions.Where(s => s.Expiry > DateTime.UtcNow).ToList();
        }

        public async Task<object?> GetUserActivityLogsAsync(string userId)
        {
            var userExists = await _mongoDb.Users.Find(u => u.Id == userId && !u.IsDeleted).AnyAsync();
            if (!userExists) return null;

            // Note: In a full production app, you would query an 'AuditLogs' collection here.
            // Since we don't have an AuditLogs collection setup yet, we return a mock array.
            return new List<object>
            {
                new { Action = "AccountCreated", Timestamp = DateTime.UtcNow.AddDays(-10), IpAddress = "192.168.1.1" },
                new { Action = "PasswordChanged", Timestamp = DateTime.UtcNow.AddDays(-2), IpAddress = "192.168.1.2" }
            };
        }
    }
}