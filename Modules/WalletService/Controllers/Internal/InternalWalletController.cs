using Microsoft.AspNetCore.Mvc;
using WalletService.Services;

namespace WalletService.Controllers.Internal
{
    [ApiController]
    [Route("api/internal/wallet")]
    // [ApiKeyAuthorize] <- You can create a custom attribute to verify a secret string in headers
    public class InternalWalletController : ControllerBase
    {
        private readonly WalletManager _walletManager;

        public InternalWalletController(WalletManager walletManager)
        {
            _walletManager = walletManager;
        }

        [HttpPost("debit")]
        public async Task<IActionResult> Debit([FromBody] DebitRequestDto request)
        {
            // Simple hardcoded check for internal service auth (Use Azure Key Vault / appsettings in prod)
            var apiKey = Request.Headers["X-Internal-Service-Key"].ToString();
            if (apiKey != "YourSuperSecretInternalMicroserviceKey123!") 
                return Unauthorized("Invalid Service Key");

            var result = await _walletManager.DebitAsync(
                request.UserId, request.Amount, request.IdempotencyKey, 
                request.ReferenceId, request.SourceService
            );

            if (!result.Success) return BadRequest(new { message = result.Message });
            return Ok(new { message = result.Message });
        }

        [HttpPost("credit")]
        public async Task<IActionResult> Credit([FromBody] CreditRequestDto request)
        {
            var apiKey = Request.Headers["X-Internal-Service-Key"].ToString();
            if (apiKey != "YourSuperSecretInternalMicroserviceKey123!") 
                return Unauthorized("Invalid Service Key");

            var result = await _walletManager.CreditAsync(
                request.UserId, request.Amount, request.IdempotencyKey, 
                request.ReferenceId, request.Description
            );

            return Ok(new { message = result.Message });
        }
    }

    public class DebitRequestDto
    {
        public string UserId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty; // e.g., "txn_buy_course_C123_U456"
        public string ReferenceId { get; set; } = string.Empty; 
        public string SourceService { get; set; } = string.Empty; 
    }
    
    public class CreditRequestDto
    {
        public string UserId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty; 
        public string Description { get; set; } = string.Empty; 
    }
}