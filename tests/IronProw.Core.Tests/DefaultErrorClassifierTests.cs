using FluentAssertions;
using Xunit;

namespace IronProw.Core.Tests;

public class DefaultErrorClassifierTests
{
    private readonly DefaultErrorClassifier _sut = new();

    [Fact]
    public void Http_and_timeout_are_retryable()
    {
        _sut.Classify(new HttpRequestException("boom")).Should().Be(ErrorClassification.Retryable);
        _sut.Classify(new TimeoutException()).Should().Be(ErrorClassification.Retryable);
    }

    [Fact]
    public void Guard_block_is_terminal()
        => _sut.Classify(new GuardException("pii")).Should().Be(ErrorClassification.Terminal);

    [Fact]
    public void Unknown_is_fallback_eligible()
        => _sut.Classify(new InvalidOperationException()).Should().Be(ErrorClassification.FallbackEligible);
}
