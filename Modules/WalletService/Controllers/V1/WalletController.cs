using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletService.Services;

namespace WalletService.Controllers.V1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize] // JWT token required
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
            return Ok(new { balance = wallet.Balance });
        }
        
        // (Optional) Add GET "transactions" endpoint here to show passbook to student
    }
}