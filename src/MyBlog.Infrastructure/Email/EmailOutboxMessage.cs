using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBlog.Application.Abstractions.Services;
using static MyBlog.Infrastructure.Persistence.Configurations.ConfigurationConstants;

namespace MyBlog.Infrastructure.Email;

internal enum EmailOutboxStatus
{
    Pending,
    Processing,
    Sent,
    Failed
}

/// <summary>
/// Transactional outbox yozuvi (domain entity emas — faqat Infrastructure). Xat avval shu jadvalga yoziladi,
/// keyin <see cref="EmailOutboxDispatcher"/> uni yuboradi.
/// </summary>
internal sealed class EmailOutboxMessage
{
    public const int ToMaxLength = 320;
    public const int SubjectMaxLength = 512;
    public const int LastErrorMaxLength = 2000;

    private EmailOutboxMessage() { }

    public Guid Id { get; private set; }
    public string To { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string HtmlBody { get; private set; } = string.Empty;
    public string? TextBody { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public EmailOutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public static EmailOutboxMessage Create(EmailMessage message, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new EmailOutboxMessage
        {
            Id = Guid.CreateVersion7(now),
            To = message.To,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            CreatedAt = now,
            Status = EmailOutboxStatus.Pending,
            NextAttemptAt = now
        };
    }

    public EmailMessage ToEmailMessage() => new(To, Subject, HtmlBody, TextBody);

    public static string? Truncate(string? error) =>
        error is null || error.Length <= LastErrorMaxLength ? error : error[..LastErrorMaxLength];
}

internal sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public const string TableName = "email_outbox";

    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.To).HasMaxLength(EmailOutboxMessage.ToMaxLength).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(EmailOutboxMessage.SubjectMaxLength).IsRequired();
        builder.Property(m => m.HtmlBody).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(EnumMaxLength).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(EmailOutboxMessage.LastErrorMaxLength);

        // Dispatcher so'rovi: status = 'Pending' AND next_attempt_at <= now.
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
    }
}
