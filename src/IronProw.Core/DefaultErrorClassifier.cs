namespace IronProw.Core;

/// <inheritdoc />
public sealed class DefaultErrorClassifier : IErrorClassifier
{
    /// <summary>
    /// Classifies an exception for fallback/retry eligibility.
    /// Returns Terminal for unrecoverable errors (guard blocks, cancellations),
    /// Retryable for transient network/timing issues, and FallbackEligible for others.
    /// </summary>
    public ErrorClassification Classify(Exception ex) => ex switch
    {
        GuardException => ErrorClassification.Terminal,
        OperationCanceledException => ErrorClassification.Terminal,
        HttpRequestException => ErrorClassification.Retryable,
        TimeoutException => ErrorClassification.Retryable,
        _ => ErrorClassification.FallbackEligible
    };
}
