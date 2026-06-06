using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace POSERP.Services
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
        public void SendEmail(string toEmail, string subject, string body)
        {
            try
            {
                string fromEmail = _configuration["EmailSettings:FromEmail"];
                string password = _configuration["EmailSettings:Password"];
                string smtpServer = _configuration["EmailSettings:SmtpServer"];
                int port = int.Parse(_configuration["EmailSettings:Port"]);

                using MailMessage mail = new MailMessage();
                mail.From = new MailAddress(fromEmail);

                foreach (var email in toEmail.Split(','))
                {
                    if (!string.IsNullOrWhiteSpace(email))
                        mail.To.Add(email.Trim());
                }

                mail.Subject = subject;
                mail.Body = body;
                mail.IsBodyHtml = true;

                using SmtpClient smtp = new SmtpClient(smtpServer, port);
                smtp.Credentials = new NetworkCredential(fromEmail, password);
                smtp.EnableSsl = true;

                smtp.Send(mail);
                _logger.LogInformation("Email sent to {ToEmail}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
            }
        }


        // ================= SYNCHRONOUS EMAIL =================
        /*  public void SendEmail(string toEmail, string subject, string body)
          {
              string fromEmail = _configuration["EmailSettings:FromEmail"];
              string password = _configuration["EmailSettings:Password"];
              string smtpServer = _configuration["EmailSettings:SmtpServer"];
              int port = int.Parse(_configuration["EmailSettings:Port"]);

              MailMessage mail = new MailMessage();

              mail.From = new MailAddress(fromEmail);

              // Multiple emails support
              foreach (var email in toEmail.Split(','))
              {
                  if (!string.IsNullOrWhiteSpace(email))
                  {
                      mail.To.Add(email.Trim());
                  }
              }

              mail.Subject = subject;
              mail.Body = body;
              mail.IsBodyHtml = true;

              SmtpClient smtp = new SmtpClient(smtpServer, port);

              smtp.Credentials = new NetworkCredential(fromEmail, password);
              smtp.EnableSsl = true;

              smtp.Send(mail);
          }*/


        // ================= ASYNC EMAIL =================
        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            string fromEmail = _configuration["EmailSettings:FromEmail"];
            string password = _configuration["EmailSettings:Password"];
            string smtpServer = _configuration["EmailSettings:SmtpServer"];
            int port = int.Parse(_configuration["EmailSettings:Port"]);

            MailMessage mail = new MailMessage();

            mail.From = new MailAddress(fromEmail);

            // Multiple emails support
            foreach (var email in toEmail.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    mail.To.Add(email.Trim());
                }
            }

            mail.Subject = subject;
            mail.Body = body;
            mail.IsBodyHtml = true;

            SmtpClient smtp = new SmtpClient(smtpServer, port);

            smtp.Credentials = new NetworkCredential(fromEmail, password);
            smtp.EnableSsl = true;

            await smtp.SendMailAsync(mail);
        }
    }


}
