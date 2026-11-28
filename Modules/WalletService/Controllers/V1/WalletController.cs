using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletService.Services;

namespace WalletService.Controllers.V1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class WalletController : ControllerBase
    {
        private readonly WalletManager _walletManager;
        private readonly IWalletReferralService? _referralService;

        public WalletController(WalletManager walletManager, IWalletReferralService? referralService = null)
        {
            _walletManager = walletManager;
            _referralService = referralService;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetMyBalance()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var wallet = await _walletManager.GetBalanceAsync(userId);
            return Ok(new { balance = wallet.Balance, updatedAt = wallet.UpdatedAt });
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetMyTransactions(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? type = null)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var (items, total) = await _walletManager.GetTransactionsAsync(userId, page, pageSize, type);
            return Ok(new
            {
                totalRecords = total,
                page = Math.Max(1, page),
                pageSize = Math.Clamp(pageSize, 1, 100),
                transactions = items
            });
        }

        [HttpGet("referral")]
        public async Task<IActionResult> GetMyReferralSummary()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            string referralCode = string.Empty;
            bool canApplyReferralCode = false;
            string? referredByCode = null;
            bool referralRewarded = false;

            if (_referralService != null)
            {
                (referralCode, canApplyReferralCode, referredByCode, referralRewarded) =
                    await _referralService.GetReferralStatusAsync(userId);
            }

            var (asReferrer, asReferred) = await _walletManager.GetReferralDataAsync(userId);
            decimal totalEarned = asReferrer.Sum(r => r.ReferrerCoins) + (asReferred?.ReferredCoins ?? 0m);

            return Ok(new
            {
                referralCode,
                rewardPerReferral = WalletManager.ReferralRewardCoins,
                totalReferrals = asReferrer.Count,
                totalCoinsEarned = totalEarned,
                referredByCode = referredByCode ?? asReferred?.ReferralCode,
                welcomeBonusEarned = asReferred?.ReferredCoins ?? (referralRewarded ? WalletManager.ReferralRewardCoins : 0m),
                canApplyReferralCode = canApplyReferralCode && asReferred == null,
                referrals = asReferrer.Select(r => new
                {
                    r.Id,
                    r.ReferredUserId,
                    r.ReferredUserName,
                    r.ReferralCode,
                    coinsEarned = r.ReferrerCoins,
                    rewardedAt = r.CreatedAt
                })
            });
        }

        [HttpPost("referral/apply")]
        public async Task<IActionResult> ApplyReferralCode([FromBody] ApplyReferralRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (_referralService == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Referral service is unavailable." });

            var (success, message, appliedCode) = await _referralService.ApplyReferralCodeAsync(userId, request.ReferralCode);
            if (!success)
                return BadRequest(new { message });

            var wallet = await _walletManager.GetBalanceAsync(userId);
            return Ok(new
            {
                message,
                appliedCode,
                rewardCoins = WalletManager.ReferralRewardCoins,
                balance = wallet.Balance
            });
        }
    }

    public class ApplyReferralRequest
    {
        [Required]
        public string ReferralCode { get; set; } = string.Empty;
    }
}
