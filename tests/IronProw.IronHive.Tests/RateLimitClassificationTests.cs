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

    // A busy self-hosted server sheds load with 503 + Retry-After. Through the registered readers (the built-in status
    // reader first, as AddIronProw registers it) the hint survives, so the gateway waits on the same provider instead of
    // falling back — the same as the OpenAI SDK client's 503.
    [Fact]
    public void A_503_with_a_retry_hint_is_retryable()
        => ClassifierWithIronHiveProvider()
            .Classify(new ProviderHttpException("busy", System.Net.HttpStatusCode.ServiceUnavailable) { RetryAfter = TimeSpan.FromSeconds(1) })
            .Should().Be(ErrorClassification.Retryable);

    [Fact]
    public void A_503_without_a_retry_hint_is_fallback_eligible()
        => ClassifierWithIronHiveProvider()
            .Classify(new ProviderHttpException("busy", System.Net.HttpStatusCode.ServiceUnavailable))
            .Should().Be(ErrorClassification.FallbackEligible);

    // Positive control for the reader order: the built-in reader alone reads the status but not the hint.
    [Fact]
    public void The_built_in_reader_alone_loses_the_hint()
        => new DefaultErrorClassifier()
            .Classify(new ProviderHttpException("busy", System.Net.HttpStatusCode.ServiceUnavailable) { RetryAfter = TimeSpan.FromSeconds(1) })
            .Should().Be(ErrorClassification.FallbackEligible);

    [Fact]
    public void Reader_reports_the_status_and_hint_of_any_http_error()
        => new IronHiveHttpFailureReader()
            .Read(new ProviderHttpException("bad gateway", System.Net.HttpStatusCode.BadGateway) { RetryAfter = TimeSpan.FromSeconds(4) })
            .Should().Be(new HttpFailure(502, TimeSpan.FromSeconds(4)));

    [Fact]
    public void Reader_reports_status_429_and_the_hint()
        => new IronHiveHttpFailureReader().Read(new RateLimitException("429") { RetryAfter = TimeSpan.FromSeconds(3) })
            .Should().Be(new HttpFailure(429, TimeSpan.FromSeconds(3)));

    // An account that cannot pay does not recover by waiting, whatever status it arrived with (OpenAI sends an exhausted
    // quota as 429): it is read as 402 and never retried on the same provider.
    [Fact]
    public void Reader_reports_a_billing_refusal_as_402_without_a_hint()
        => new IronHiveHttpFailureReader().Read(new BillingException("You exceeded your current quota"))
            .Should().Be(new HttpFailure(402, null));

    [Fact]
    public void A_billing_refusal_is_not_retried()
        => ClassifierWithIronHiveProvider().Classify(new BillingException("insufficient_quota"))
            .Should().Be(ErrorClassification.FallbackEligible);
}
