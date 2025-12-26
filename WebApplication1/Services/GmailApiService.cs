using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using MimeKit;
using System.Text;

namespace AMS.Services
{
    public class GmailApiService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GmailApiService> _logger;

        public GmailApiService(IConfiguration configuration, ILogger<GmailApiService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private GmailService GetGmailService()
        {
            var gmailConfig = _configuration.GetSection("GmailApi");

            var clientId = gmailConfig["ClientId"];
            var clientSecret = gmailConfig["ClientSecret"];
            var refreshToken = gmailConfig["RefreshToken"];

            if (string.IsNullOrEmpty(clientId) || clientId.StartsWith("YOUR_"))
            {
                throw new InvalidOperationException("Gmail API not configured. Please set up OAuth credentials in appsettings.json");
            }

            var credential = new UserCredential(
                new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow(
                    new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow.Initializer
                    {
                        ClientSecrets = new ClientSecrets
                        {
                            ClientId = clientId,
                            ClientSecret = clientSecret
                        }
                    }),
                "user",
                new Google.Apis.Auth.OAuth2.Responses.TokenResponse
                {
                    RefreshToken = refreshToken
                });

            return new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Attendo"
            });
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
        {
            try
            {
                var gmailConfig = _configuration.GetSection("GmailApi");
                var fromEmail = gmailConfig["FromEmail"];
                var fromName = gmailConfig["FromName"] ?? "Attendo";

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, fromEmail));
                message.To.Add(new MailboxAddress("", toEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder();
                if (isHtml)
                {
                    bodyBuilder.HtmlBody = body;
                }
                else
                {
                    bodyBuilder.TextBody = body;
                }
                message.Body = bodyBuilder.ToMessageBody();

                using var memoryStream = new MemoryStream();
                await message.WriteToAsync(memoryStream);
                var rawMessage = Convert.ToBase64String(memoryStream.ToArray())
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Replace("=", "");

                var gmailMessage = new Message { Raw = rawMessage };

                var service = GetGmailService();
                await service.Users.Messages.Send(gmailMessage, "me").ExecuteAsync();

                _logger.LogInformation("Email sent successfully to {Email} via Gmail API", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email} via Gmail API", toEmail);
                return false;
            }
        }

        public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var subject = "Reset Your Password - Attendo";
            var body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;'>
                        <h1 style='color: white; margin: 0;'>Password Reset Request</h1>
                    </div>
                    <div style='background: #f7f7f7; padding: 30px; border-radius: 0 0 10px 10px;'>
                        <p style='color: #333; font-size: 16px;'>Hello,</p>
                        <p style='color: #666; font-size: 14px;'>
                            You recently requested to reset your password for your Attendo account. 
                            Click the button below to reset it.
                        </p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{resetLink}' 
                               style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); 
                                      color: white; 
                                      padding: 15px 40px; 
                                      text-decoration: none; 
                                      border-radius: 8px; 
                                      display: inline-block;
                                      font-weight: bold;'>
                                Reset Password
                            </a>
                        </div>
                        <p style='color: #999; font-size: 12px;'>
                            If you didn't request a password reset, please ignore this email or contact support if you have concerns.
                        </p>
                        <p style='color: #999; font-size: 12px;'>
                            This link will expire in 24 hours for security reasons.
                        </p>
                        <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;'>
                        <p style='color: #999; font-size: 11px; text-align: center;'>
                            © 2025 Attendo. All rights reserved.
                        </p>
                    </div>
                </div>";

            return await SendEmailAsync(toEmail, subject, body);
        }

        public async Task<bool> SendWelcomeEmailAsync(string toEmail, string userName, string password)
        {
            var subject = "Welcome to Attendo - Your Account Details";
            var body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;'>
                        <h1 style='color: white; margin: 0;'>Welcome to Attendo!</h1>
                    </div>
                    <div style='background: #f7f7f7; padding: 30px; border-radius: 0 0 10px 10px;'>
                        <p style='color: #333; font-size: 16px;'>Hello {userName},</p>
                        <p style='color: #666; font-size: 14px;'>
                            Your account has been created successfully. Here are your login credentials:
                        </p>
                        <div style='background: white; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                            <p style='margin: 10px 0; color: #333;'><strong>Email:</strong> {toEmail}</p>
                            <p style='margin: 10px 0; color: #333;'><strong>Temporary Password:</strong> {password}</p>
                        </div>
                        <p style='color: #d9534f; font-size: 13px; background: #fff3cd; padding: 15px; border-radius: 5px;'>
                            <strong>⚠️ Important:</strong> Please change your password after your first login for security reasons.
                        </p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{_configuration["AppUrl"]}/Auth/Login' 
                               style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); 
                                      color: white; 
                                      padding: 15px 40px; 
                                      text-decoration: none; 
                                      border-radius: 8px; 
                                      display: inline-block;
                                      font-weight: bold;'>
                                Login Now
                            </a>
                        </div>
                        <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;'>
                        <p style='color: #999; font-size: 11px; text-align: center;'>
                            © 2025 Attendo. All rights reserved.
                        </p>
                    </div>
                </div>";

            return await SendEmailAsync(toEmail, subject, body);
        }
    }
}
