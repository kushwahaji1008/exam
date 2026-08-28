using System.Text.Json;
using System.Text;

namespace EnrollmentService.Clients
{
    public class WalletServiceClient
    {
        private readonly HttpClient _httpClient;

        public WalletServiceClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(bool Success, string Message)> DebitCoinsAsync(string userId, decimal amount, string idempotencyKey, string referenceId)
        {
            var payload = new
            {
                UserId = userId,
                Amount = amount,
                IdempotencyKey = idempotencyKey,
                ReferenceId = referenceId,
                SourceService = "EnrollmentService"
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            
            // Call the internal endpoint we created in WalletService
            var response = await _httpClient.PostAsync("/api/internal/wallet/debit", content);
            
            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return (true, "Payment successful via Wallet.");
            }

            // Extract error message from WalletService if any
            try
            {
                var errorObj = JsonSerializer.Deserialize<JsonElement>(responseString);
                var errorMsg = errorObj.GetProperty("message").GetString();
                return (false, errorMsg ?? "Wallet deduction failed.");
            }
            catch
            {
                return (false, "Wallet deduction failed due to a network error.");
            }
        }
    }
}