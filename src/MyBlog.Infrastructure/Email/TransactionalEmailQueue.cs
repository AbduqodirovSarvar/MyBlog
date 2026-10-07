using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Email;

/// <summary>
/// Scoped <see cref="IEmailQueue"/>: so'rov davomida DB tranzaksiyasi ochiq bo'lsa, xatlar commit'gacha ushlab turiladi
/// va faqat commit muvaffaqiyatli bo'lgandan keyin asosiy navbatga uzatiladi; rollback bo'lsa tashlab yuboriladi.
/// Shu tufayli bekor qilingan amal (masalan izoh) uchun xat ketib qolmaydi.
/// Bir vaqtning o'zida DbContext uchun transaction interceptor sifatida ham ro'yxatdan o'tadi.
/// </summary>
internal sealed class TransactionalEmailQueue(ChannelEmailQueue inner) : DbTransactionInterceptor, IEmailQueue
{
    private readonly List<EmailMessage> _pending = [];
    private int _activeTransactions;

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (_activeTransactions == 0)
            return inner.EnqueueAsync(message, cancellationToken);

        _pending.Add(message);
        return ValueTask.CompletedTask;
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        _activeTransactions++;
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        _activeTransactions++;
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        FlushAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        FlushAsync(cancellationToken).AsTask();

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Discard();

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard();
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Discard();

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard();
        return Task.CompletedTask;
    }

    private async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        _activeTransactions = Math.Max(0, _activeTransactions - 1);
        if (_activeTransactions > 0)
            return;

        var messages = _pending.ToArray();
        _pending.Clear();

        foreach (var message in messages)
            await inner.EnqueueAsync(message, cancellationToken);
    }

    private void Discard()
    {
        _activeTransactions = Math.Max(0, _activeTransactions - 1);
        if (_activeTransactions == 0)
            _pending.Clear();
    }
}
