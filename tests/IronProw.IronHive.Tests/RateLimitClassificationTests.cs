using AwesomeAssertions;
using IronHive.Abstractions.Exceptions;
using IronProw.Core;
using Xunit;

namespace IronProw.IronHive.Tests;

/// <summary>
/// Regression coverage for the cross-repo rate-limit classification concern this adapter was
/// once suspected to mishandle: ironhive normalizes every provider's HTTP 429 (and vendor
/// equivalents) into <see cref="RateLimitException"/> before it ever reaches iron-prow — the
/// exception mappers wired into every native message generator's live call path throw it in
/// place of a bare <see cref="HttpRequestException"/>. Because <see cref="RateLimitException"/>
/// derives from <see cref="HiveException"/> (not <see cref="HttpRequestException"/>),
/// <see cref="DefaultErrorClassifier"/>'s blanket "HttpRequestException is Retryable" rule never
/// matches it — it falls through to the default FallbackEligible arm, which is exactly the
/// desired outcome (advance to the next provider instead of retrying the rate-limited one). This
/// test locks that emergent behavior so a future edit to the classifier's HttpRequestException
/// case cannot silently widen it to also catch RateLimitException.
/// </summary>
public class RateLimitClassificationTests
{
    private readonly DefaultErrorClassifier _sut = new();

    [Fact]
    public void RateLimitException_is_fallback_eligible_not_retryable()
        => _sut.Classify(new RateLimitException("429 Too Many Requests"))
            .Should().Be(ErrorClassification.FallbackEligible);
}
