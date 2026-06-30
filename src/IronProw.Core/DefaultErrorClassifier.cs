namespace IronProw.Core;

/// <inheritdoc />
public sealed class DefaultErrorClassifier : IErrorClassifier
{
    /// <summary>
    /// Classifies an exception for fallback/retry eligibility.
    /// Returns Terminal for unrecoverable errors (guard blocks, user cancellations),
    /// Retryable for transient network/timing issues (including HttpClient-timeout cancellations), and FallbackEligible for others.
    /// Note: HttpClient request timeouts surface as TaskCanceledException with an inner TimeoutException — these are retryable.
    /// </summary>
    public ErrorClassification Classify(Exception ex) => ex switch
    {
        GuardException => ErrorClassification.Terminal,
        // HttpClient request timeout surfaces as TaskCanceledException with an inner TimeoutException -> retryable.
        TaskCanceledException { InnerException: TimeoutException } => ErrorClassification.Retryable,
        // Any other cancellation (user token) -> terminal, never retried or fallen back.
        OperationCanceledException => ErrorClassification.Terminal,
        HttpRequestException => ErrorClassification.Retryable,
        TimeoutException => ErrorClassification.Retryable,
        _ => ErrorClassification.FallbackEligible
    };
}
