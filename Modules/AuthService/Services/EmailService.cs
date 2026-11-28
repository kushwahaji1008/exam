
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace AuthService.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(
            string toEmail,
            string subject,
            string body)
        {
            var provider = _configuration["Email:Provider"] ?? "Log";

            // Log provider
            if (provider.Equals("Log", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "[Email:Log] To={Email}, Subject={Subject}",
                    toEmail,
                    subject);

                return;
            }

            // Validate provider
            if (!provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unsupported email provider: {provider}");
            }

            var smtpSettings = _configuration.GetSection("SmtpSettings");

            var server = smtpSettings["Server"];
            var senderEmail = smtpSettings["SenderEmail"];
            var appPassword = smtpSettings["AppPassword"];
            var senderName = smtpSettings["SenderName"] ?? "Exam Solution";

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(senderEmail) ||
                string.IsNullOrWhiteSpace(appPassword))
            {
                throw new InvalidOperationException(
                    "SMTP configuration is incomplete. Check SmtpSettings.");
            }

            if (!int.TryParse(smtpSettings["Port"], out var port))
            {
                port = 587;
            }

            var message = new MimeMessage();

            message.From.Add(
                new MailboxAddress(senderName, senderEmail));

            message.To.Add(
                MailboxAddress.Parse(toEmail));

            message.Subject = subject;

            message.Body = new TextPart(TextFormat.Html)
            {
                Text = body
            };

            using var client = new SmtpClient
            {
                Timeout = 15000
            };

            try
            {
                var secureOption = port == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;

                await client.ConnectAsync(
                    server,
                    port,
                    secureOption);

                await client.AuthenticateAsync(
                    senderEmail,
                    appPassword);

                await client.SendAsync(message);

                _logger.LogInformation(
                    "Email successfully sent to {Email}",
                    toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed sending email via SMTP to {Email}",
                    toEmail);

                throw;
            }
            finally
            {
                if (client.IsConnected)
                {
                    try
                    {
                        await client.DisconnectAsync(true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "SMTP disconnection failed.");
                    }
                }
            }
        }
    }
}
