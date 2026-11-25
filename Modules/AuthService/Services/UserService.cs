using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuthService.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AuthService.Services
{
    public class UserService
    {
        private readonly MongoDbService _mongoDb;
        private readonly AuditService? _auditService;

        public UserService(MongoDbService mongoDb, AuditService? auditService = null)
        {
            _mongoDb = mongoDb;
            _auditService = auditService;
        }

        // ==========================================
        // 1. CORE CRUD OPERATIONS
        // ==========================================

        public async Task<object> GetAllUsersAsync(UserFilterRequest filter)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 100);

            var builder = Builders<User>.Filter;
            var mongoFilter = builder.Eq(u => u.IsDeleted, false);

            if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
            {
                var escaped = Regex.Escape(filter.SearchQuery.Trim());
                var searchFilter = builder.Or(
                    builder.Regex(u => u.Email, new BsonRegularExpression(escaped, "i")),
                    builder.Regex(u => u.FullName, new BsonRegularExpression(escaped, "i")),
                    builder.Regex(u => u.UserId, new BsonRegularExpression(escaped, "i"))
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
                .SortByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return new
            {
                TotalRecords = totalRecords,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
                Data = users.Select(AuthenticationService.ToUserDto).ToList()
            };
        }

        public async Task<(bool Success, string Message, string? UserId)> CreateUserAsync(CreateUserAdminRequest request)
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var exists = await _mongoDb.Users.Find(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted).AnyAsync();
            if (exists) return (false, "User with this email already exists.", null);

            string uniqueNineDigitId = string.Empty;
            bool isIdUnique = false;
            while (!isIdUnique)
            {
                uniqueNineDigitId = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();
                var idExists = await _mongoDb.Users.Find(u => u.UserId == uniqueNineDigitId).AnyAsync();
                if (!idExists) isIdUnique = true;
            }

            var user = new User
            {
                UserId = uniqueNineDigitId,
                Email = normalizedEmail,
                UserName = normalizedEmail.Split('@')[0],
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FullName = request.FullName,
                Role = request.Role,
                IsActive = request.IsActive,
                IsEmailVerified = request.IsEmailVerified,
                IsApproved = true,
                CreatedAt = DateTime.UtcNow,
                Sessions = new List<ActiveSession>()
            };

            await _mongoDb.Users.InsertOneAsync(user);

            if (_auditService != null)
                await _auditService.LogAuditAsync(user.UserId, "UserCreatedByAdmin", $"Admin created user {user.Email} with role {user.Role}");

            return (true, "User created successfully.", user.UserId);
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user != null)
            {
                user.PasswordHash = "HIDDEN";
                user.MfaSecret = null;
                user.MfaRecoveryCodes = new List<string>();
                user.Otp = null;
                user.PhoneOtp = null;
                user.Sessions = new List<ActiveSession>();
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

            if (!request.IsActive)
            {
                update = update.Set(u => u.Sessions, new List<ActiveSession>());
            }

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, "UserUpdatedByAdmin", $"Updated profile/status/role ({request.Role})");

            return result.MatchedCount > 0;
        }

        public async Task<bool> PatchUserAsync(string userId, object request)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null || request == null) return false;

            try
            {
                var json = JsonSerializer.Serialize(request);
                var patchDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (patchDict == null || patchDict.Count == 0) return false;

                var updates = new List<UpdateDefinition<User>>();

                foreach (var kvp in patchDict)
                {
                    var key = kvp.Key.ToLowerInvariant();
                    var val = kvp.Value;

                    switch (key)
                    {
                        case "fullname" when val.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<User>.Update.Set(u => u.FullName, val.GetString() ?? user.FullName));
                            break;
                        case "phone":
                            updates.Add(Builders<User>.Update.Set(u => u.Phone, val.ValueKind == JsonValueKind.Null ? null : val.GetString()));
                            break;
                        case "gender" when val.ValueKind == JsonValueKind.String:
                            updates.Add(Builders<User>.Update.Set(u => u.Gender, val.GetString() ?? string.Empty));
                            break;
                        case "dateofbirth":
                            if (val.ValueKind == JsonValueKind.Null)
                                updates.Add(Builders<User>.Update.Set(u => u.DateOfBirth, null));
                            else if (val.ValueKind == JsonValueKind.String && val.TryGetDateTime(out var dob))
                                updates.Add(Builders<User>.Update.Set(u => u.DateOfBirth, dob));
                            break;
                        case "profilepicture":
                            updates.Add(Builders<User>.Update.Set(u => u.ProfilePicture, val.ValueKind == JsonValueKind.Null ? null : val.GetString()));
                            break;
                        case "isactive" when val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False:
                            var active = val.GetBoolean();
                            updates.Add(Builders<User>.Update.Set(u => u.IsActive, active));
                            if (!active) updates.Add(Builders<User>.Update.Set(u => u.Sessions, new List<ActiveSession>()));
                            break;
                        case "isemailverified" when val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False:
                            updates.Add(Builders<User>.Update.Set(u => u.IsEmailVerified, val.GetBoolean()));
                            break;
                        case "requirespasswordreset" when val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False:
                            updates.Add(Builders<User>.Update.Set(u => u.RequiresPasswordReset, val.GetBoolean()));
                            break;
                    }
                }

                if (updates.Count == 0) return false;

                var result = await _mongoDb.Users.UpdateOneAsync(
                    u => u.UserId == userId && !u.IsDeleted,
                    Builders<User>.Update.Combine(updates));

                if (result.MatchedCount > 0 && _auditService != null)
                    await _auditService.LogAuditAsync(userId, "UserPatchedByAdmin", "Admin patched allowed user fields.");

                return result.MatchedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ApproveUserAsync(string userId)
        {
            var update = Builders<User>.Update.Set(u => u.IsApproved, true);

            var result = await _mongoDb.Users.UpdateOneAsync(
                u => u.UserId == userId && !u.IsDeleted && !u.IsApproved,
                update
            );

            if (result.ModifiedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, "UserApproved", "Account approved by admin.");

            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.IsDeleted, true)
                .Set(u => u.IsActive, false)
                .Set(u => u.Sessions, new List<ActiveSession>());

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.ModifiedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, "UserDeleted", "Account soft-deleted by admin.");

            return result.ModifiedCount > 0;
        }

        // ==========================================
        // 2. ACCOUNT STATUS MANAGEMENT
        // ==========================================

        public async Task<bool> SetUserActiveStatusAsync(string userId, bool isActive)
        {
            var update = Builders<User>.Update.Set(u => u.IsActive, isActive);
            if (!isActive)
            {
                update = update.Set(u => u.Sessions, new List<ActiveSession>());
            }

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, isActive ? "UserActivated" : "UserDeactivated", $"Account active state set to {isActive}");

            return result.MatchedCount > 0;
        }

        public async Task<bool> SetUserLockStatusAsync(string userId, bool isLocked)
        {
            var update = Builders<User>.Update
                .Set(u => u.IsLocked, isLocked)
                .Set(u => u.FailedLoginAttempts, 0);

            if (isLocked)
            {
                update = update.Set(u => u.LockoutEnd, DateTime.UtcNow.AddYears(100))
                               .Set(u => u.Sessions, new List<ActiveSession>());
            }
            else
            {
                update = update.Set(u => u.LockoutEnd, null);
            }

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogSecurityEventAsync(userId, isLocked ? "AccountLockedByAdmin" : "AccountUnlockedByAdmin", isLocked ? "Warning" : "Info", $"Admin set lock={isLocked}");

            return result.MatchedCount > 0;
        }

        // ==========================================
        // 3. SECURITY & VERIFICATION
        // ==========================================

        public async Task<bool> ForcePasswordResetAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.RequiresPasswordReset, true)
                .Set(u => u.Sessions, new List<ActiveSession>());

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "ForcePasswordReset", "Warning", "Admin forced password reset and terminated active sessions.");

            return result.MatchedCount > 0;
        }

        public async Task<bool> VerifyUserEmailManuallyAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.IsEmailVerified, true)
                .Set(u => u.Otp, null)
                .Set(u => u.OtpExpiry, null)
                .Set(u => u.OtpAttemptCount, 0);

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogAuditAsync(userId, "EmailVerifiedByAdmin", "Admin manually verified email.");

            return result.MatchedCount > 0;
        }

        public async Task<bool> ResetUserMfaAsync(string userId)
        {
            var update = Builders<User>.Update
                .Set(u => u.MfaEnabled, false)
                .Set(u => u.MfaSecret, null)
                .Set(u => u.MfaRecoveryCodes, new List<string>());

            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "MfaResetByAdmin", "Warning", "Admin reset MFA for user.");

            return result.MatchedCount > 0;
        }

        public async Task<bool> RevokeAllUserSessionsAsync(string userId)
        {
            var update = Builders<User>.Update.Set(u => u.Sessions, new List<ActiveSession>());
            var result = await _mongoDb.Users.UpdateOneAsync(u => u.UserId == userId && !u.IsDeleted, update);
            if (result.MatchedCount > 0 && _auditService != null)
                await _auditService.LogSecurityEventAsync(userId, "AllSessionsRevokedByAdmin", "Info", "Admin revoked all sessions.");

            return result.MatchedCount > 0;
        }

        // ==========================================
        // 4. AUDIT & ACTIVITY
        // ==========================================

        public async Task<List<ActiveSession>?> GetUserSessionsAsync(string userId)
        {
            var user = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).FirstOrDefaultAsync();
            if (user == null) return null;

            return user.Sessions.Where(s => s.Expiry > DateTime.UtcNow).ToList();
        }

        public async Task<object?> GetUserActivityLogsAsync(string userId)
        {
            var userExists = await _mongoDb.Users.Find(u => u.UserId == userId && !u.IsDeleted).AnyAsync();
            if (!userExists) return null;

            var logs = await _mongoDb.AuditLogs
                .Find(a => a.UserId == userId)
                .SortByDescending(a => a.Timestamp)
                .Limit(50)
                .ToListAsync();

            return logs.Select(l => new
            {
                id = l.Id,
                action = l.ActionType,
                details = l.Details,
                timestamp = l.Timestamp,
                ipAddress = l.IpAddress,
                userAgent = l.UserAgent
            }).ToList();
        }
    }
}
