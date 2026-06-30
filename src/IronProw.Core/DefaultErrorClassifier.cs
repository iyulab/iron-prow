namespace IronProw.Core;

/// <inheritdoc />
public sealed class DefaultErrorClassifier : IErrorClassifier
{
    /// <inheritdoc />
    public ErrorClassification Classify(Exception ex) => ex switch
    {
        GuardException => ErrorClassification.Terminal,
        HttpRequestException => ErrorClassification.Retryable,
        TimeoutException => ErrorClassification.Retryable,
        _ => ErrorClassification.FallbackEligible
    };
}
