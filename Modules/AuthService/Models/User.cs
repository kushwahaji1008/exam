using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace AuthService.Models
{
    // ==========================================
    // DATABASE ENTITIES
    // ==========================================

    [BsonIgnoreExtraElements]
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        public string? Phone { get; set; }

        [Required]
        public UserRole Role { get; set; } = UserRole.Student;

        // Dynamic roles assigned by Admin (supports /roles endpoints)
        public List<string> AssignedRoleIds { get; set; } = new();
        public List<string> Permissions { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsEmailVerified { get; set; } = false;
        public bool IsPhoneVerified { get; set; } = false;
        public bool IsDeleted { get; set; } = false;

        // Security & Account Lockout
        public bool IsLocked { get; set; } = false;
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }
        public bool RequiresPasswordReset { get; set; } = false;

        // Multi-Factor Authentication (MFA)
        public bool MfaEnabled { get; set; } = false;
        public string? MfaSecret { get; set; }
        public List<string> MfaRecoveryCodes { get; set; } = new();

        // Profile details
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string? ProfilePicture { get; set; }

        // Gamification 
        public int TotalPoints { get; set; } = 0;
        public int CurrentStreak { get; set; } = 0;
        public UserBadge Badge { get; set; } = UserBadge.Bronze;

        // OTP Fields for Email Verification
        public string? Otp { get; set; }
        public DateTime? OtpExpiry { get; set; }

        // Sessions Management (Replaces single RefreshToken fields for multi-device support)
        public List<ActiveSession> Sessions { get; set; } = new();
    }

    public class ActiveSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime Expiry { get; set; }
        public string? DeviceInfo { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Role
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> PermissionIds { get; set; } = new();
    }

    public class Permission
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    // ==========================================
    // ENUMS
    // ==========================================

    public enum UserRole
    {
        Student = 0,
        Teacher = 1,
        Admin = 2,
        SuperAdmin = 3
    }

    public enum UserBadge
    {
        Bronze,
        Silver,
        Gold,
        Diamond,
        Heroic,
        GrandMaster
    }

    // ==========================================
    // AUTHENTICATION REQUEST DTOs
    // ==========================================

    public class RegisterRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(6)] public string Password { get; set; } = string.Empty;
        [Required] public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public UserRole Role { get; set; } = UserRole.Student;
    }

    public class LoginRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        [Required] public string Token { get; set; } = string.Empty; // Expired JWT
        [Required] public string RefreshToken { get; set; } = string.Empty;
    }

    public class VerifyOtpRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Otp { get; set; } = string.Empty;
    }

    public class ResendOtpRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Otp { get; set; } = string.Empty;
        [Required, MinLength(6)] public string NewPassword { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        [Required] public string OldPassword { get; set; } = string.Empty;
        [Required, MinLength(6)] public string NewPassword { get; set; } = string.Empty;
    }

    public class CheckAvailabilityRequest
    {
        [Required] public string Value { get; set; } = string.Empty;
    }

    public class ValidateTokenRequest
    {
        [Required] public string Token { get; set; } = string.Empty;
    }

    public class RevokeTokenRequest
    {
        [Required] public string Token { get; set; } = string.Empty;
    }

    public class ChangeEmailRequest
    {
        [Required, EmailAddress] public string NewEmail { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
    }

    public class AddPhoneRequest
    {
        [Required, Phone] public string PhoneNumber { get; set; } = string.Empty;
    }

    // ==========================================
    // MULTI-FACTOR AUTHENTICATION DTOs
    // ==========================================

    public class MfaSetupRequest
    {
        [Required] public string Method { get; set; } = "Authenticator"; // Authenticator, Email, SMS
    }

    public class MfaVerifyRequest
    {
        [Required] public string Code { get; set; } = string.Empty;
    }

    public class MfaChallengeRequest
    {
        [Required] public string UserId { get; set; } = string.Empty;
        [Required] public string Code { get; set; } = string.Empty;
    }

    // ==========================================
    // USER PROFILE DTOs
    // ==========================================

    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public UserRole Role { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? ProfilePicture { get; set; }
        public bool IsEmailVerified { get; set; }
        public bool MfaEnabled { get; set; }
    }

    public class UpdateProfileRequest
    {
        [Required] public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string? ProfilePicture { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public UserDto User { get; set; } = null!;
        public bool RequiresMfa { get; set; } = false;
    }

    // ==========================================
    // ADMIN / MANAGEMENT DTOs
    // ==========================================

    public class CreateUserAdminRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        
        [Required] public string Password { get; set; } = string.Empty;
        [Required] public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; } = UserRole.Student;
        public bool IsActive { get; set; } = true;
        public bool IsEmailVerified { get; set; } = true;
    }

    public class UpdateUserAdminRequest
    {
        [Required] public string FullName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; }
        public bool IsEmailVerified { get; set; }
    }

    public class UserFilterRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SearchQuery { get; set; }
        public UserRole? Role { get; set; }
        public bool? IsActive { get; set; }
    }

    // ==========================================
    // ROLE & PERMISSION DTOs
    // ==========================================

    public class CreateRoleRequest
    {
        [Required] public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class UpdateRoleRequest
    {
        [Required] public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class AssignPermissionsRequest
    {
        [Required] public List<string> PermissionIds { get; set; } = new();
    }

    public class CreatePermissionRequest
    {
        [Required] public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class UpdatePermissionRequest
    {
        [Required] public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    // ==========================================
    // AUDIT & SECURITY DTOs
    // ==========================================

    public class AuditFilterRequest
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? ActionType { get; set; }
        public string? UserId { get; set; }
    }

    public class SecurityEventFilterRequest : AuditFilterRequest
    {
        public string? EventLevel { get; set; } // Info, Warning, Critical
    }

    public class AuditLog
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty; // e.g., "UserCreated", "ProfileUpdated"
        public string Details { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class SecurityEvent
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty; // e.g., "FailedLogin", "PasswordReset", "MfaFailed"
        public string EventLevel { get; set; } = "Info"; // Info, Warning, Critical
        public string Details { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}