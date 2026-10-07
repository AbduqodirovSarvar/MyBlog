using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.BackgroundJobs;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Email;

/// <summary>
/// Outbox qatorlarini qayta ishlash (singleton, holatsiz; har amal o'z DI scope'i va DbContext'ida).
/// Yetkazish kafolati — at-least-once: xat yuborilib, Sent deb belgilashdan oldin jarayon qulasa, qulf muddati
/// o'tgach qator qayta yuboriladi. Bir nechta instance bir xatni bir vaqtda olmaydi (FOR UPDATE SKIP LOCKED).
/// </summary>
internal sealed partial class EmailOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailOutboxProcessor> logger)
{
    private const string Pending = nameof(EmailOutboxStatus.Pending);
    private const string Processing = nameof(EmailOutboxStatus.Processing);
    private const string Failed = nameof(EmailOutboxStatus.Failed);

    /// <summary>
    /// Qulf muddati o'tgan Processing qatorlarini (instance yuborish paytida qulagan) qaytaradi: urinishlar tugagan
    /// bo'lsa Failed, aks holda darhol Pending.
    /// </summary>
    public async Task<int> RecoverStuckAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();

        var recovered = await db.Database.ExecuteSqlAsync(
            $"""
             UPDATE email_outbox
             SET status = CASE WHEN attempts >= {options.Value.MaxAttempts} THEN {Failed} ELSE {Pending} END,
                 next_attempt_at = {now},
                 locked_until = NULL,
                 last_error = COALESCE(last_error, 'Processing lock expired')
             WHERE status = {Processing} AND locked_until < {now}
             """,
            cancellationToken);

        if (recovered > 0)
            LogRecovered(logger, recovered);

        return recovered;
    }

    /// <summary>Bitta batch'ni egallab yuboradi; egallangan qatorlar sonini qaytaradi.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var claimed = await ClaimAsync(settings, cancellationToken);

        foreach (var message in claimed)
            await SendAsync(message, settings, cancellationToken);

        return claimed.Count;
    }

    /// <summary>Saqlash muddati o'tgan Sent qatorlarini o'chiradi.</summary>
    public async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var threshold = timeProvider.GetUtcNow().AddDays(-options.Value.RetentionDays);

        return await db.Set<EmailOutboxMessage>()
            .Where(m => m.Status == EmailOutboxStatus.Sent && m.SentAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Bitta atomik UPDATE ... RETURNING: Pending qatorlarni Processing'ga o'tkazadi, qulf qo'yadi va urinishni sanaydi.
    /// SKIP LOCKED tufayli parallel instance'lar bir-birini kutmaydi va bir xil qatorni olmaydi.
    /// </summary>
    private async Task<List<EmailOutboxMessage>> ClaimAsync(EmailOutboxOptions settings, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var lockedUntil = now.AddSeconds(settings.LockSeconds);

        return await db.Set<EmailOutboxMessage>()
            .FromSql(
                $"""
                 UPDATE email_outbox AS o
                 SET status = {Processing}, locked_until = {lockedUntil}, attempts = o.attempts + 1
                 FROM (
                     SELECT id FROM email_outbox
                     WHERE status = {Pending} AND next_attempt_at <= {now}
                     ORDER BY next_attempt_at
                     LIMIT {settings.BatchSize}
                     FOR UPDATE SKIP LOCKED
                 ) AS c
                 WHERE o.id = c.id AND o.status = {Pending}
                 RETURNING o.*
                 """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task SendAsync(EmailOutboxMessage message, EmailOutboxOptions settings, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.Set<EmailOutboxMessage>().Where(m => m.Id == message.Id && m.Status == EmailOutboxStatus.Processing);

        try
        {
            await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message.ToEmailMessage(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var error = EmailOutboxMessage.Truncate($"{ex.GetType().Name}: {ex.Message}");

            if (message.Attempts >= settings.MaxAttempts)
            {
                await rows.ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, EmailOutboxStatus.Failed)
                    .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(m => m.LastError, error), CancellationToken.None);
                LogFailedPermanently(logger, ex, message.Id, message.Attempts);
                return;
            }

            var delay = EmailOutboxBackoff.GetDelay(message.Attempts);
            await rows.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, EmailOutboxStatus.Pending)
                .SetProperty(m => m.NextAttemptAt, now + delay)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(m => m.LastError, error), CancellationToken.None);
            LogRetryScheduled(logger, ex, message.Id, message.Attempts, delay);
            return;
        }

        // Yuborildi: belgilash bekor qilinmasin (aks holda qulf o'tgach xat qayta ketadi).
        await rows.ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, EmailOutboxStatus.Sent)
            .SetProperty(m => m.SentAt, timeProvider.GetUtcNow())
            .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
            .SetProperty(m => m.LastError, (string?)null), CancellationToken.None);
        LogSent(logger, message.Id, message.Attempts);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox email {MessageId} sent (attempt {Attempt})")]
    private static partial void LogSent(ILogger logger, Guid messageId, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox email {MessageId} failed (attempt {Attempt}); retrying in {Delay}")]
    private static partial void LogRetryScheduled(ILogger logger, Exception exception, Guid messageId, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox email {MessageId} failed permanently after {Attempts} attempts")]
    private static partial void LogFailedPermanently(ILogger logger, Exception exception, Guid messageId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovered {Count} outbox email(s) with an expired processing lock")]
    private static partial void LogRecovered(ILogger logger, int count);
}

/// <summary>
/// Outbox'ni so'rovchi fon servisi: signal (yangi xat) yoki PollIntervalSeconds bo'yicha uyg'onadi, to'liq batch
/// kelsa darhol keyingisini oladi; soatiga bir marta eski Sent qatorlarini tozalaydi.
/// </summary>
internal sealed partial class EmailOutboxDispatcher(
    EmailOutboxProcessor processor,
    EmailOutboxSignal signal,
    IOptions<EmailOutboxOptions> options,
    IOptions<BackgroundJobsOptions> jobsOptions,
    TimeProvider timeProvider,
    ILogger<EmailOutboxDispatcher> logger) : BackgroundService
{
    internal static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!jobsOptions.Value.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        var settings = options.Value;
        var pollInterval = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
        var lastCleanup = DateTimeOffset.MinValue;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await processor.RecoverStuckAsync(stoppingToken);

                    while (await processor.ProcessBatchAsync(stoppingToken) >= settings.BatchSize)
                    {
                        // To'liq batch — navbatda yana bo'lishi mumkin.
                    }

                    if (timeProvider.GetUtcNow() - lastCleanup >= CleanupInterval)
                    {
                        await processor.CleanupAsync(stoppingToken);
                        lastCleanup = timeProvider.GetUtcNow();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogLoopFailed(logger, ex);
                }

                await signal.WaitAsync(pollInterval, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ilova to'xtamoqda.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email outbox dispatcher is disabled (background jobs are off)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email outbox dispatcher iteration failed")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);
}
