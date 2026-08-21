using AuthService.Models;

namespace AuthService.Services
{
    public interface IPhoneService
    {
        Task<(bool Success, string Message)> AddPhoneAsync(string userId, AddPhoneRequest request);
        Task<(bool Success, string Message)> VerifyPhoneAsync(string userId, string otp);
        Task<(bool Success, string Message)> ChangePhoneAsync(string userId, AddPhoneRequest request);
        Task<(bool Success, string Message)> RemovePhoneAsync(string userId);
        Task<(bool Success, string Message)> ResendPhoneOtpAsync(string userId);
    }
}