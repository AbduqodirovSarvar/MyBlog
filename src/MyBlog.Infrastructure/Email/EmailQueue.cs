using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.Email;

/// <summary>Xotiradagi chegaralangan navbat (singleton). To'lsa yozuvchi kutadi.</summary>
internal sealed class ChannelEmailQueue : IEmailQueue
{
    public const int Capacity = 1000;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _channel.Writer.WriteAsync(message, cancellationToken);
    }
}

/// <summary>Navbatdagi email'larni yuboradi: 3 urinish, eksponensial kutish; xatolar loglanadi.</summary>
internal sealed class EmailDispatcherHostedService(
    ChannelEmailQueue queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<EmailDispatcherHostedService> logger) : BackgroundService
{
    internal const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
                await SendWithRetryAsync(message, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ilova to'xtamoqda.
        }
    }

    private async Task SendWithRetryAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await sender.SendAsync(message, cancellationToken);
                logger.LogInformation("Email '{Subject}' sent to {Recipient}", message.Subject, message.To);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == MaxAttempts)
                {
                    logger.LogError(ex, "Failed to send email '{Subject}' to {Recipient} after {Attempts} attempts",
                        message.Subject, message.To, attempt);
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                logger.LogWarning(ex, "Email '{Subject}' to {Recipient} failed (attempt {Attempt}); retrying in {Delay}",
                    message.Subject, message.To, attempt, delay);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }
}
