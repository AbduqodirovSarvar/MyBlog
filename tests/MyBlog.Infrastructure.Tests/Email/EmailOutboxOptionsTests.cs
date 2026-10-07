using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Infrastructure.Email;

namespace MyBlog.Infrastructure.Tests.Email;

public sealed class EmailOutboxOptionsTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    [InlineData(4, 120)]
    [InlineData(5, 720)]
    [InlineData(6, 720)]
    [InlineData(50, 720)]
    public void Backoff_follows_schedule_and_caps_at_twelve_hours(int attempts, int expectedMinutes) =>
        EmailOutboxBackoff.GetDelay(attempts).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));

    [Fact]
    public void Backoff_treats_non_positive_attempts_as_first() =>
        EmailOutboxBackoff.GetDelay(0).ShouldBe(TimeSpan.FromMinutes(1));

    [Fact]
    public void Defaults_are_valid() =>
        Resolve([]).ShouldSatisfyAllConditions(
            o => o.PollIntervalSeconds.ShouldBe(10),
            o => o.BatchSize.ShouldBe(20),
            o => o.MaxAttempts.ShouldBe(8),
            o => o.LockSeconds.ShouldBe(120),
            o => o.RetentionDays.ShouldBe(14));

    [Theory]
    [InlineData(nameof(EmailOutboxOptions.PollIntervalSeconds), "0")]
    [InlineData(nameof(EmailOutboxOptions.BatchSize), "0")]
    [InlineData(nameof(EmailOutboxOptions.MaxAttempts), "0")]
    [InlineData(nameof(EmailOutboxOptions.LockSeconds), "5")]
    [InlineData(nameof(EmailOutboxOptions.RetentionDays), "0")]
    public void Out_of_range_values_fail_validation(string key, string value)
    {
        var ex = Should.Throw<OptionsValidationException>(() => Resolve(new() { [$"{EmailOutboxOptions.SectionName}:{key}"] = value }));
        ex.Message.ShouldContain(key);
    }

    [Fact]
    public void Outbox_message_maps_to_and_from_email_message()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new EmailMessage("a@b.c", "S", "<p>H</p>", "T");

        var row = EmailOutboxMessage.Create(message, now);

        row.ToEmailMessage().ShouldBe(message);
        row.Status.ShouldBe(EmailOutboxStatus.Pending);
        row.NextAttemptAt.ShouldBe(now);
        row.Attempts.ShouldBe(0);
        EmailOutboxMessage.Truncate(new string('x', 5000))!.Length.ShouldBe(EmailOutboxMessage.LastErrorMaxLength);
    }

    private static EmailOutboxOptions Resolve(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddValidatedOptions<EmailOutboxOptions>(EmailOutboxOptions.SectionName);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<EmailOutboxOptions>>().Value;
    }
}
