using System.ComponentModel.DataAnnotations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Email;

internal sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>Ulanishdan boshlab TLS (odatda 465-port).</summary>
    public bool UseSsl { get; set; }

    /// <summary>STARTTLS (odatda 587-port).</summary>
    public bool UseStartTls { get; set; }

    public string? UserName { get; set; }
    public string? Password { get; set; }

    [Required, EmailAddress]
    public string FromAddress { get; set; } = "no-reply@myblog.local";

    public string FromName { get; set; } = "MyBlog";
}

/// <summary>MailKit orqali SMTP yuborish. To'g'ridan-to'g'ri emas, IEmailQueue orqali ishlatish tavsiya etiladi.</summary>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        var socketOptions = settings switch
        {
            { UseSsl: true } => SecureSocketOptions.SslOnConnect,
            { UseStartTls: true } => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(settings.UserName))
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
