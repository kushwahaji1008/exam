using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using WalletService.Services;

namespace WalletService.Controllers.Internal
{
    [ApiController]
    [Route("api/internal/wallet")]
    public class InternalWalletController : ControllerBase
    {
        private readonly WalletManager _walletManager;
        private readonly IConfiguration _configuration;

        public InternalWalletController(WalletManager walletManager, IConfiguration configuration)
        {
            _walletManager = walletManager;
            _configuration = configuration;
        }

        private bool IsAuthorizedInternalCall(out IActionResult? failureResult)
        {
            var expectedKey = _configuration["InternalAuth:ServiceKey"]
                              ?? Environment.GetEnvironmentVariable("INTERNAL_SERVICE_KEY");
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                failureResult = StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Internal service key is not configured." });
                return false;
            }

            var providedKey = Request.Headers["X-Internal-Service-Key"].ToString();
            if (string.IsNullOrEmpty(providedKey))
            {
                failureResult = Unauthorized(new { message = "Missing internal service key." });
                return false;
            }

            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));
            var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, providedHash))
            {
                failureResult = Unauthorized(new { message = "Invalid service key." });
                return false;
            }

            failureResult = null;
            return true;
        }

        [HttpPost("debit")]
        public async Task<IActionResult> Debit([FromBody] DebitRequestDto request)
        {
            if (!IsAuthorizedInternalCall(out var failure)) return failure!;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var result = await _walletManager.DebitAsync(
                request.UserId, request.Amount, request.IdempotencyKey,
                request.ReferenceId, request.SourceService);

            if (!result.Success) return BadRequest(new { message = result.Message });
            return Ok(new { message = result.Message });
        }

        [HttpPost("credit")]
        public async Task<IActionResult> Credit([FromBody] CreditRequestDto request)
        {
            if (!IsAuthorizedInternalCall(out var failure)) return failure!;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var result = await _walletManager.CreditAsync(
                request.UserId, request.Amount, request.IdempotencyKey,
                request.ReferenceId, request.Description);

            if (!result.Success) return BadRequest(new { message = result.Message });
            return Ok(new { message = result.Message });
        }
    }

    public class DebitRequestDto
    {
        [Required] public string UserId { get; set; } = string.Empty;
        [Range(0.01, 10000000)] public decimal Amount { get; set; }
        [Required] public string IdempotencyKey { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string SourceService { get; set; } = string.Empty;
    }

    public class CreditRequestDto
    {
        [Required] public string UserId { get; set; } = string.Empty;
        [Range(0.01, 10000000)] public decimal Amount { get; set; }
        [Required] public string IdempotencyKey { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
