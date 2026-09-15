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
        // A refused connection or an unresolvable host is not transient on the time scale of a retry
        // (the endpoint is down, not busy) -> degrade to the next provider instead of paying the retry
        // budget; the health tracker then keeps the dead provider demoted.
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError }
            => ErrorClassification.FallbackEligible,
        HttpRequestException => ErrorClassification.Retryable,
        TimeoutException => ErrorClassification.Retryable,
        _ => ErrorClassification.FallbackEligible
    };
}
