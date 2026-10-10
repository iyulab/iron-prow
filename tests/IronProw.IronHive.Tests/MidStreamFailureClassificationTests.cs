using System.Net;
using AwesomeAssertions;
using IronHive.Abstractions.Exceptions;
using IronProw.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronProw.IronHive.Tests;

/// <summary>
/// A vendor error that arrives inside a stream that had already started has no HTTP status of its own; ironhive raises it
/// as <see cref="ProviderResponseException"/> with the status the vendor documents for the same error outside a stream.
/// The gateway classifies it as that HTTP error: Anthropic's mid-stream <c>overloaded_error</c> is a capacity signal (529,
/// fallen back like a 503), OpenAI's <c>server_error</c> a transient fault (500, retried), a mid-stream
/// <c>invalid_request_error</c> a refusal (400, never retried). Before, every one of them fell back the same way.
/// </summary>
public class MidStreamFailureClassificationTests
{
    private static IErrorClassifier ClassifierWithIronHiveProvider()
    {
        var services = new ServiceCollection();
        services.AddIronProw().AddIronHiveAnthropic("anthropic", 10, "claude", c => c.ApiKey = "test");
        return services.BuildServiceProvider().GetRequiredService<IErrorClassifier>();
    }

    [Theory]
    [InlineData(500, ErrorClassification.Retryable)]
    [InlineData(504, ErrorClassification.Retryable)]
    [InlineData(529, ErrorClassification.FallbackEligible)]
    [InlineData(400, ErrorClassification.FallbackEligible)]
    public void A_mid_stream_failure_is_classified_as_its_equivalent_HTTP_error(int status, ErrorClassification expected)
    {
        var classifier = ClassifierWithIronHiveProvider();

        var midStream = classifier.Classify(new ProviderResponseException("stream failed") { EquivalentStatusCode = (HttpStatusCode)status });
        var outsideStream = classifier.Classify(new ProviderHttpException("failed", (HttpStatusCode)status));

        midStream.Should().Be(expected);
        midStream.Should().Be(outsideStream, "the same vendor error is handled the same way inside and outside a stream");
    }

    [Fact]
    public void A_529_with_a_retry_hint_is_retryable_like_a_503()
        => ClassifierWithIronHiveProvider()
            .Classify(new ProviderHttpException("overloaded", (HttpStatusCode)529) { RetryAfter = TimeSpan.FromSeconds(1) })
            .Should().Be(ErrorClassification.Retryable);

    [Fact]
    public void A_mid_stream_failure_without_a_documented_status_keeps_falling_back()
        => ClassifierWithIronHiveProvider()
            .Classify(new ProviderResponseException("stream ended without completion"))
            .Should().Be(ErrorClassification.FallbackEligible);
}
