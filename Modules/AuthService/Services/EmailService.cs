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

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var provider = _configuration["Email:Provider"] ?? "Log";
            if (!provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[Email:Log] To={Email}, Subject={Subject}", toEmail, subject);
                return;
            }

            var smtpSettings = _configuration.GetSection("SmtpSettings");
            var server = smtpSettings["Server"];
            var senderEmail = smtpSettings["SenderEmail"];
            var appPassword = smtpSettings["AppPassword"];
            var senderName = smtpSettings["SenderName"] ?? "Exam Solution";

            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(appPassword))
            {
                _logger.LogWarning("SMTP settings are incomplete; falling back to logging email for {Email}.", toEmail);
                return;
            }

            int port = int.TryParse(smtpSettings["Port"], out var parsedPort) ? parsedPort : 587;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart(TextFormat.Html) { Text = body };

            using var client = new SmtpClient { Timeout = 10000 };

            try
            {
                await client.ConnectAsync(server, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(senderEmail, appPassword);
                await client.SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending email via SMTP to {Email}", toEmail);
            }
            finally
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true);
                }
            }
        }
    }
}