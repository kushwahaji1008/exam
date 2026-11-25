using System.Net;
using System.Net.Mail;

namespace NotificationService.Services
{
    public class EmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
            {
                _logger.LogWarning("Cannot send notification email: invalid recipient '{Recipient}'", toEmail);
                return false;
            }

            try
            {
                var provider = _configuration["Email:Provider"] ?? "Log";
                if (!provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[NotificationEmail:Log] To={Email}, Subject={Subject}", toEmail, subject);
                    return true;
                }

                var server = _configuration["SmtpSettings:Server"];
                var senderEmail = _configuration["SmtpSettings:SenderEmail"];
                var appPassword = _configuration["SmtpSettings:AppPassword"];
                var senderName = _configuration["SmtpSettings:SenderName"] ?? "Exam Solution";

                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(appPassword))
                {
                    _logger.LogWarning("SMTP settings incomplete for NotificationService; skipping send to {Email}", toEmail);
                    return false;
                }

                int port = int.TryParse(_configuration["SmtpSettings:Port"], out var p) ? p : 587;
                using var smtpClient = new SmtpClient(server, port)
                {
                    Credentials = new NetworkCredential(senderEmail, appPassword),
                    EnableSsl = true,
                    Timeout = 10000
                };

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                await smtpClient.SendMailAsync(mailMessage);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification email to {Email}", toEmail);
                return false;
            }
        }
    }
}
