using System.Data.Common;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Infrastructure.Email;

/// <summary>
/// Scoped <see cref="IEmailQueue"/>: xatni <c>email_outbox</c> jadvaliga so'rovning o'z <see cref="AppDbContext"/>i
/// orqali to'g'ridan-to'g'ri <c>INSERT</c> bilan yozadi (change tracker va SaveChanges ishlatilmaydi — domain event
/// handler'lari SavedChanges ichida chaqiradi, qayta SaveChanges xavfli).
/// <list type="bullet">
/// <item>Tranzaksiya ochiq bo'lsa (ExecuteInTransactionAsync, domain event'lar shu ichida) — INSERT o'sha tranzaksiyaga
/// qo'shiladi: biznes o'zgarishi bilan birga commit/rollback bo'ladi (atomik, aynan bir marta yoziladi).</item>
/// <item>Tranzaksiya yo'q bo'lsa (masalan forgot-password, yoki commit'dan keyingi chaqiruvlar) — INSERT darhol
/// autocommit bilan saqlanadi. Bunda oldingi commit va INSERT orasida kichik oyna bor: jarayon aynan shu yerda
/// qulasa xat yo'qoladi (biznes o'zgarishi saqlangan, xat yo'q). Jadvalga tushgan xat esa qayta ishga tushishdan
/// keyin ham yo'qolmaydi.</item>
/// </list>
/// Transaction interceptor sifatida faqat dispatcher'ni uyg'otish vaqtini boshqaradi: tranzaksiya ichida yozilgan
/// xat commit'dan keyin ko'rinadi, shuning uchun signal commit'da beriladi.
/// </summary>
internal sealed class OutboxEmailQueue(IServiceProvider services, EmailOutboxSignal signal, TimeProvider timeProvider)
    : DbTransactionInterceptor, IEmailQueue
{
    private bool _signalOnCommit;

    public async ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Lazy: AppDbContext opsiyalari shu interceptor'ni oladi — konstruktorda so'rash aylanma bog'liqlik bo'lardi.
        var db = services.GetRequiredService<AppDbContext>();
        var row = EmailOutboxMessage.Create(message, timeProvider.GetUtcNow());

        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO email_outbox (id, "to", subject, html_body, text_body, created_at, status, attempts, next_attempt_at)
             VALUES ({row.Id}, {row.To}, {row.Subject}, {row.HtmlBody}, {row.TextBody}, {row.CreatedAt},
                     {row.Status.ToString()}, 0, {row.NextAttemptAt})
             """,
            cancellationToken);

        if (db.Database.CurrentTransaction is null)
            signal.Notify();
        else
            _signalOnCommit = true;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => OnCommitted();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        OnCommitted();
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        _signalOnCommit = false;

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _signalOnCommit = false;
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        _signalOnCommit = false;

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _signalOnCommit = false;
        return Task.CompletedTask;
    }

    private void OnCommitted()
    {
        if (!_signalOnCommit)
            return;

        _signalOnCommit = false;
        signal.Notify();
    }
}

/// <summary>Dispatcher'ni yangi xat haqida darhol uyg'otish (singleton, bir nechta signal bittaga birlashadi).</summary>
internal sealed class EmailOutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Signal yoki <paramref name="timeout"/> — qaysi biri oldin bo'lsa.</summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (_channel.Reader.TryRead(out _))
            return;

        using var timeoutCts = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await _channel.Reader.WaitToReadAsync(linked.Token);
            _channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout — navbatdagi poll.
        }
    }
}
