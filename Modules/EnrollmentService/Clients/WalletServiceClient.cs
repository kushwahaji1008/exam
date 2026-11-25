using System.Text;
using System.Text.Json;

namespace EnrollmentService.Clients
{
    public class WalletServiceClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public WalletServiceClient(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
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

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/internal/wallet/debit")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var serviceKey = _configuration["InternalAuth:ServiceKey"] ?? Environment.GetEnvironmentVariable("INTERNAL_SERVICE_KEY");
            if (!string.IsNullOrWhiteSpace(serviceKey))
            {
                requestMessage.Headers.TryAddWithoutValidation("X-Internal-Service-Key", serviceKey);
            }

            var response = await _httpClient.SendAsync(requestMessage);
            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return (true, "Payment successful via Wallet.");
            }

            try
            {
                var errorObj = JsonSerializer.Deserialize<JsonElement>(responseString);
                if (errorObj.TryGetProperty("message", out var msgProp))
                {
                    return (false, msgProp.GetString() ?? "Wallet deduction failed.");
                }
            }
            catch
            {
                // Non-JSON response
            }

            return (false, "Wallet deduction failed.");
        }
    }
}
