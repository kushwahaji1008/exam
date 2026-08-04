using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace AuthService.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var smtpSettings = _configuration.GetSection("SmtpSettings");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(smtpSettings["SenderName"], smtpSettings["SenderEmail"]));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart(TextFormat.Html) { Text = body };

            using var client = new SmtpClient();
            
            try
            {
                // Connect to Gmail SMTP
                await client.ConnectAsync(
                    smtpSettings["Server"], 
                    int.Parse(smtpSettings["Port"]!), 
                    SecureSocketOptions.StartTls
                );

                // Authenticate using Gmail App Password
                await client.AuthenticateAsync(
                    smtpSettings["SenderEmail"], 
                    smtpSettings["AppPassword"]
                );

                await client.SendAsync(message);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }
    }
}