using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace AuthService.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        public string? Phone { get; set; }

        [Required]
        public UserRole Role { get; set; } = UserRole.Student;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastLoginAt { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsEmailVerified { get; set; } = false;

        public bool IsPhoneVerified { get; set; } = false;

        public bool IsDeleted { get; set; } = false;
        
        public string Gender { get; set; } = string.Empty;

        public DateTime? DateOfBirth { get; set;} 

        public string? ProfilePicture { get; set; }

        public List<string> Permissions { get; set; } = new();

        // Learning 
        

        // Gamification 
        public int TotalPoints { get; set; } = 0;
        public int CurrentStreak { get; set; } = 0;
        public UserBadge Badge { get; set; } = UserBadge.Bronze;

        // OTP Fields for Email Verification
        public string? Otp { get; set; }
        public DateTime? OtpExpiry { get; set; }

        // Refresh Token Fields
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiry { get; set; }
    }

    public enum UserRole
    {
        Student,
        Teacher,
        Admin,
        SuperAdmin
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

    public class RegisterRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public UserRole Role { get; set; } = UserRole.Student;
    }

    public class LoginRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    // Update LoginResponse to include RefreshToken
    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public UserDto User { get; set; } = null!;
    }

    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public UserRole Role { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? ProfilePicture { get; set; }
    }

    public class VerifyOtpRequest
    {
        public string Email { get; set; }
        public string Otp { get; set; }
    }

    public class UpdateProfileRequest
    {
        [Required]
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string? ProfilePicture { get; set; }
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

    public class RefreshTokenRequest
    {
        [Required] public string Token { get; set; } = string.Empty; // Expired JWT
        [Required] public string RefreshToken { get; set; } = string.Empty;
    }

    
}