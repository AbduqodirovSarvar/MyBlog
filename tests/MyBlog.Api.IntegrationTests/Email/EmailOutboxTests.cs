using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Api.IntegrationTests.Comments;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.DataIsolation;
using MyBlog.Infrastructure.Email;
using MyBlog.Infrastructure.Persistence;

namespace MyBlog.Api.IntegrationTests.Email;

/// <summary>Sozlanadigan soxta SMTP: yuborilganlarni yozib boradi, kerak bo'lsa kechikadi yoki xato beradi.</summary>
internal sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public bool Fail { get; set; }

    public TimeSpan Delay { get; set; }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);

        if (Fail)
            throw new InvalidOperationException("SMTP is down");

        _sent.Enqueue(message);
    }
}

/// <summary>Alohida baza; fon job'lari o'chiq (dispatcher ishlamaydi) — processor testdan to'g'ridan-to'g'ri chaqiriladi.</summary>
internal sealed class EmailOutboxTestHost : IAsyncDisposable
{
    public const int MaxAttempts = 3;

    private readonly string _connectionString;

    private EmailOutboxTestHost(string connectionString, bool runDispatcher)
    {
        _connectionString = connectionString;
        Factory = new ApiFactory(connectionString).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("EmailOutbox:MaxAttempts", MaxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (runDispatcher)
            {
                // Poll juda siyrak — xat faqat signal orqali tez yuborilishi mumkin.
                builder.UseSetting("BackgroundJobs:Enabled", "true");
                builder.UseSetting("EmailOutbox:PollIntervalSeconds", "3600");
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Sender);
            });
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    public FakeEmailSender Sender { get; } = new();

    public EmailOutboxProcessor Processor => Factory.Services.GetRequiredService<EmailOutboxProcessor>();

    public static EmailOutboxTestHost Start(PostgresFixture postgres, bool runDispatcher = false)
    {
        var host = new EmailOutboxTestHost(postgres.ConnectionStringFor($"outbox_{Guid.NewGuid():N}"), runDispatcher);
        _ = host.Factory.Services; // host ishga tushadi → migratsiyalar qo'llanadi
        return host;
    }

    public EmailOutboxProcessor NewProcessor() => ActivatorUtilities.CreateInstance<EmailOutboxProcessor>(Factory.Services);

    public async Task EnqueueAsync(EmailMessage message)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEmailQueue>().EnqueueAsync(message, TestContext.Current.CancellationToken);
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task<List<EmailOutboxMessage>> RowsAsync() =>
        QueryAsync(db => db.Set<EmailOutboxMessage>().AsNoTracking().OrderBy(m => m.Id).ToListAsync(TestContext.Current.CancellationToken));

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();

        await using var db = new AppDbContext(AppDbContextFactory.CreateDesignTimeOptions(_connectionString),
            DisabledDataIsolationContext.Instance);
        await db.Database.EnsureDeletedAsync();
    }
}

[Collection(PostgresCollection.Name)]
public sealed class EmailOutboxTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EmailMessage Message(string subject = "Hello", string? text = null) =>
        new("user@example.com", subject, $"<p>{subject}</p>", text);

    [Fact]
    public async Task Enqueue_inside_committed_transaction_persists_row_on_commit()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);

        await using (var scope = host.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);

            await scope.ServiceProvider.GetRequiredService<IEmailQueue>().EnqueueAsync(Message(text: "Hello"), Ct);

            // Commit'gacha boshqa ulanishga ko'rinmaydi.
            (await host.RowsAsync()).ShouldBeEmpty();

            await transaction.CommitAsync(Ct);
        }

        var row = (await host.RowsAsync()).ShouldHaveSingleItem();
        row.To.ShouldBe("user@example.com");
        row.Subject.ShouldBe("Hello");
        row.HtmlBody.ShouldBe("<p>Hello</p>");
        row.TextBody.ShouldBe("Hello");
        row.Status.ShouldBe(EmailOutboxStatus.Pending);
        row.Attempts.ShouldBe(0);
        row.Id.Version.ShouldBe(7);
    }

    [Fact]
    public async Task Enqueue_inside_rolled_back_transaction_leaves_no_row()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);

        await using (var scope = host.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<IEmailQueue>().EnqueueAsync(Message(), Ct);
            await transaction.RollbackAsync(Ct);
        }

        (await host.RowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Enqueue_without_transaction_persists_row_immediately()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);

        await host.EnqueueAsync(Message());

        var row = (await host.RowsAsync()).ShouldHaveSingleItem();
        row.TextBody.ShouldBeNull();
        row.Status.ShouldBe(EmailOutboxStatus.Pending);
        row.NextAttemptAt.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Comment_creation_writes_notification_to_outbox_in_same_transaction()
    {
        postgres.SkipIfUnavailable();
        await using var host = await CommentsTestHost.StartAsync(postgres);
        using var alice = host.Client(host.AliceId);

        var response = await alice.PostAsJsonAsync($"/api/posts/{host.PostId}/comments", new { content = "Zo'r!" }, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));

        var rows = await host.QueryAsync(db => db.Set<EmailOutboxMessage>().AsNoTracking().ToListAsync(Ct));
        rows.ShouldHaveSingleItem().To.ShouldBe("owner@test.local");
    }

    [Fact]
    public async Task Processor_sends_pending_rows_and_marks_them_sent()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);
        for (var i = 0; i < 3; i++)
            await host.EnqueueAsync(Message($"m{i}"));

        (await host.Processor.ProcessBatchAsync(Ct)).ShouldBe(3);
        (await host.Processor.ProcessBatchAsync(Ct)).ShouldBe(0);

        host.Sender.Sent.Select(m => m.Subject).Order().ShouldBe(["m0", "m1", "m2"]);
        foreach (var row in await host.RowsAsync())
        {
            row.Status.ShouldBe(EmailOutboxStatus.Sent);
            row.Attempts.ShouldBe(1);
            row.SentAt.ShouldNotBeNull();
            row.LockedUntil.ShouldBeNull();
            row.LastError.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Failing_sender_schedules_retry_with_backoff_and_marks_failed_after_max_attempts()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);
        host.Sender.Fail = true;
        await host.EnqueueAsync(Message());

        var before = DateTimeOffset.UtcNow;
        (await host.Processor.ProcessBatchAsync(Ct)).ShouldBe(1);

        var row = (await host.RowsAsync()).ShouldHaveSingleItem();
        row.Status.ShouldBe(EmailOutboxStatus.Pending);
        row.Attempts.ShouldBe(1);
        row.NextAttemptAt.ShouldBeGreaterThanOrEqualTo(before + EmailOutboxBackoff.GetDelay(1) - TimeSpan.FromSeconds(1));
        row.LockedUntil.ShouldBeNull();
        row.LastError!.ShouldContain("SMTP is down");

        // Vaqti kelmagan qator olinmaydi.
        (await host.Processor.ProcessBatchAsync(Ct)).ShouldBe(0);

        for (var attempt = 2; attempt <= EmailOutboxTestHost.MaxAttempts; attempt++)
        {
            await MakeDueAsync(host);
            (await host.Processor.ProcessBatchAsync(Ct)).ShouldBe(1);
        }

        row = (await host.RowsAsync()).ShouldHaveSingleItem();
        row.Status.ShouldBe(EmailOutboxStatus.Failed);
        row.Attempts.ShouldBe(EmailOutboxTestHost.MaxAttempts);

        await MakeDueAsync(host);
        (await host.Processor.ProcessBatchAsync(Ct)).ShouldBe(0);
        host.Sender.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Parallel_processors_never_send_the_same_message_twice()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);
        const int count = 60;
        for (var i = 0; i < count; i++)
            await host.EnqueueAsync(Message($"m{i}"));
        host.Sender.Delay = TimeSpan.FromMilliseconds(5);

        var first = host.NewProcessor();
        var second = host.NewProcessor();
        var claimed = await Task.WhenAll(DrainAsync(first), DrainAsync(second), DrainAsync(first), DrainAsync(second));

        claimed.Sum().ShouldBe(count);
        host.Sender.Sent.Count.ShouldBe(count);
        host.Sender.Sent.Select(m => m.Subject).Distinct().Count().ShouldBe(count);
        (await host.RowsAsync()).ShouldAllBe(r => r.Status == EmailOutboxStatus.Sent && r.Attempts == 1);

        static async Task<int> DrainAsync(EmailOutboxProcessor processor)
        {
            var total = 0;
            int claimedNow;
            while ((claimedNow = await processor.ProcessBatchAsync(Ct)) > 0)
                total += claimedNow;
            return total;
        }
    }

    [Fact]
    public async Task Stuck_processing_rows_with_expired_lock_are_recovered()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);
        await host.EnqueueAsync(Message("expired"));
        await host.EnqueueAsync(Message("locked"));
        await host.EnqueueAsync(Message("exhausted"));

        var past = DateTimeOffset.UtcNow.AddMinutes(-5);
        var future = DateTimeOffset.UtcNow.AddMinutes(5);
        await SetProcessingAsync(host, "expired", past, attempts: 1);
        await SetProcessingAsync(host, "locked", future, attempts: 1);
        await SetProcessingAsync(host, "exhausted", past, attempts: EmailOutboxTestHost.MaxAttempts);

        (await host.Processor.RecoverStuckAsync(Ct)).ShouldBe(2);

        var rows = (await host.RowsAsync()).ToDictionary(r => r.Subject);
        rows["expired"].Status.ShouldBe(EmailOutboxStatus.Pending);
        rows["expired"].LockedUntil.ShouldBeNull();
        rows["locked"].Status.ShouldBe(EmailOutboxStatus.Processing);
        rows["exhausted"].Status.ShouldBe(EmailOutboxStatus.Failed);

        (await host.Processor.ProcessBatchAsync(Ct)).ShouldBe(1);
        host.Sender.Sent.ShouldHaveSingleItem().Subject.ShouldBe("expired");
        (await host.RowsAsync()).Single(r => r.Subject == "expired").Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Running_dispatcher_wakes_up_on_enqueue_and_sends()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres, runDispatcher: true);

        // Tranzaksiya ichida: signal commit'da beriladi.
        await using (var scope = host.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<IEmailQueue>().EnqueueAsync(Message("tx"), Ct);
            await transaction.CommitAsync(Ct);
        }

        await host.EnqueueAsync(Message("plain"));

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (host.Sender.Sent.Count < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(50, Ct);

        host.Sender.Sent.Select(m => m.Subject).Order().ShouldBe(["plain", "tx"]);
        var rows = await host.RowsAsync();
        while (rows.Any(r => r.Status != EmailOutboxStatus.Sent) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, Ct);
            rows = await host.RowsAsync();
        }

        rows.ShouldAllBe(r => r.Status == EmailOutboxStatus.Sent);
    }

    [Fact]
    public async Task Cleanup_deletes_only_sent_rows_older_than_retention()
    {
        postgres.SkipIfUnavailable();
        await using var host = EmailOutboxTestHost.Start(postgres);
        await host.EnqueueAsync(Message("old"));
        await host.EnqueueAsync(Message("recent"));
        await host.EnqueueAsync(Message("pending"));

        await host.QueryAsync(db => db.Set<EmailOutboxMessage>()
            .Where(m => m.Subject != "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, EmailOutboxStatus.Sent).SetProperty(m => m.SentAt, DateTimeOffset.UtcNow), Ct));
        await host.QueryAsync(db => db.Set<EmailOutboxMessage>()
            .Where(m => m.Subject == "old")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.SentAt, DateTimeOffset.UtcNow.AddDays(-30)), Ct));

        (await host.Processor.CleanupAsync(Ct)).ShouldBe(1);
        (await host.RowsAsync()).Select(r => r.Subject).Order().ShouldBe(["pending", "recent"]);
    }

    private static Task<int> MakeDueAsync(EmailOutboxTestHost host) =>
        host.QueryAsync(db => db.Set<EmailOutboxMessage>()
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Ct));

    private static Task<int> SetProcessingAsync(EmailOutboxTestHost host, string subject, DateTimeOffset lockedUntil, int attempts) =>
        host.QueryAsync(db => db.Set<EmailOutboxMessage>()
            .Where(m => m.Subject == subject)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, EmailOutboxStatus.Processing)
                .SetProperty(m => m.LockedUntil, lockedUntil)
                .SetProperty(m => m.Attempts, attempts), Ct));
}
