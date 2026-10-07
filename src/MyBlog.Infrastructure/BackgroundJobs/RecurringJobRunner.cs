using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;

namespace MyBlog.Infrastructure.BackgroundJobs;

internal sealed class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>Testlarda o'chiriladi.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Ro'yxatdan o'tgan barcha IRecurringJob'larni mustaqil sikllarda ishga tushiradi. Har bir bajarilish
/// yangi DI scope'da; xatolar loglanadi va sikl davom etadi. Bir job'ning bajarilishlari ustma-ust tushmaydi.
/// </summary>
internal sealed class RecurringJobRunner(
    IServiceScopeFactory scopeFactory,
    IOptions<BackgroundJobsOptions> options,
    TimeProvider timeProvider,
    ILogger<RecurringJobRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Background jobs are disabled");
            return;
        }

        List<(Type Type, string Name, TimeSpan Interval)> jobs;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            jobs = scope.ServiceProvider.GetServices<IRecurringJob>()
                .Select(j => (j.GetType(), j.Name, j.Interval))
                .ToList();
        }

        foreach (var job in jobs.Where(j => j.Interval <= TimeSpan.Zero))
            logger.LogWarning("Recurring job {JobName} has a non-positive interval and will not run", job.Name);

        await Task.WhenAll(jobs.Where(j => j.Interval > TimeSpan.Zero).Select(j => RunLoopAsync(j.Type, j.Name, j.Interval, stoppingToken)));
    }

    private async Task RunLoopAsync(Type jobType, string name, TimeSpan interval, CancellationToken stoppingToken)
    {
        logger.LogInformation("Recurring job {JobName} scheduled every {Interval}", name, interval);
        using var timer = new PeriodicTimer(interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ExecuteOnceAsync(jobType, name, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ilova to'xtamoqda.
        }
    }

    private async Task ExecuteOnceAsync(Type jobType, string name, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var job = scope.ServiceProvider.GetServices<IRecurringJob>().First(j => j.GetType() == jobType);

            var started = timeProvider.GetTimestamp();
            await job.ExecuteAsync(stoppingToken);
            logger.LogDebug("Recurring job {JobName} completed in {Elapsed}", name, timeProvider.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recurring job {JobName} failed", name);
        }
    }
}
