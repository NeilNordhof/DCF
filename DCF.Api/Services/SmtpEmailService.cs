using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DCF.Api.Services;

public class EmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1025;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Drum Corps Fantasy";
    public bool StartTls { get; set; } = false;
    public string FrontendUrl { get; set; } = string.Empty;
    public string UnsubscribeSecret { get; set; } = string.Empty;
}

public class SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger) : IEmailService
{
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var opts = options.Value;

        var message = new MimeMessage();

        message.From.Add(new MailboxAddress(opts.FromName, opts.FromAddress));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();

        var socketOptions = opts.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

        await client.ConnectAsync(opts.Host, opts.Port, socketOptions);

        if (!string.IsNullOrEmpty(opts.Username))
        {
            await client.AuthenticateAsync(opts.Username, opts.Password);
        }

        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
    }
}
