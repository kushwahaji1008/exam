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

        public WalletController(WalletManager walletManager)
        {
            _walletManager = walletManager;
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
        public async Task<IActionResult> GetMyTransactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var (items, total) = await _walletManager.GetTransactionsAsync(userId, page, pageSize);
            return Ok(new
            {
                totalRecords = total,
                page = Math.Max(1, page),
                pageSize = Math.Clamp(pageSize, 1, 100),
                transactions = items
            });
        }
    }
}
