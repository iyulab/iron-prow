using AwesomeAssertions;
using IronHive.Abstractions.Exceptions;
using IronProw.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronProw.IronHive.Tests;

/// <summary>
/// Ironhive normalizes every provider's HTTP 429 (and vendor equivalents) into <see cref="RateLimitException"/>, which
/// is not an HTTP exception type — so the gateway only knows it as a rate limit through the
/// <see cref="IronHiveHttpFailureReader"/> the <c>AddIronHive*</c> methods register. The classification then matches
/// a raw SDK client's 429: with no retry hint it degrades to the next provider at once (a blind retry rarely outlasts
/// a rate limit — this was the adapter's behavior before the reader existed, and it is kept); with a hint the gateway
/// waits it on the same provider, up to <see cref="ResilienceOptions.MaxRetryAfter"/>.
/// </summary>
public class RateLimitClassificationTests
{
    private static IErrorClassifier ClassifierWithIronHiveProvider()
    {
        var services = new ServiceCollection();
        services.AddIronProw().AddIronHiveAnthropic("anthropic", 10, "claude", c => c.ApiKey = "test");
        return services.BuildServiceProvider().GetRequiredService<IErrorClassifier>();
    }

    [Fact]
    public void RateLimitException_without_a_retry_hint_is_fallback_eligible()
        => ClassifierWithIronHiveProvider().Classify(new RateLimitException("429 Too Many Requests"))
            .Should().Be(ErrorClassification.FallbackEligible);

    [Fact]
    public void RateLimitException_with_a_retry_hint_is_retryable()
        => ClassifierWithIronHiveProvider()
            .Classify(new RateLimitException("429 Too Many Requests") { RetryAfter = TimeSpan.FromSeconds(2) })
            .Should().Be(ErrorClassification.Retryable);

    [Fact]
    public void Without_the_reader_a_rate_limit_is_not_seen_as_one()
        => new DefaultErrorClassifier()
            .Classify(new RateLimitException("429") { RetryAfter = TimeSpan.FromSeconds(2) })
            .Should().Be(ErrorClassification.FallbackEligible);

    [Fact]
    public void Reader_reports_status_429_and_the_hint()
        => new IronHiveHttpFailureReader().Read(new RateLimitException("429") { RetryAfter = TimeSpan.FromSeconds(3) })
            .Should().Be(new HttpFailure(429, TimeSpan.FromSeconds(3)));
}
