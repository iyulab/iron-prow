namespace IronProw.Core;

/// <inheritdoc />
/// <remarks>
/// An exception that describes an HTTP failure (see <see cref="IHttpFailureReader"/>) is classified by its status:
/// <list type="table">
/// <listheader><term>Status</term><description>Classification</description></listheader>
/// <item><term>408, 500, 502, 504</term><description><see cref="ErrorClassification.Retryable"/> — a transient fault on the same provider.</description></item>
/// <item><term>429, 503</term><description><see cref="ErrorClassification.Retryable"/> when the provider sent a retry hint
/// (<see cref="ResilienceChatClient"/> waits it, up to <see cref="ResilienceOptions.MaxRetryAfter"/>); otherwise
/// <see cref="ErrorClassification.FallbackEligible"/> — a capacity signal with no end in sight is not worth a blind retry.</description></item>
/// <item><term>any other status</term><description><see cref="ErrorClassification.FallbackEligible"/> — credentials, a model the
/// provider does not serve, or a request this provider rejects (a 400 from one provider is not evidence another rejects it).</description></item>
/// </list>
/// A consumer whose providers give a status a domain meaning (for example a local provider that answers 409 until a
/// download is approved) decorates this classifier for those codes. After the per-provider retries are spent a
/// retryable failure also degrades to the next provider.
/// </remarks>
public sealed class DefaultErrorClassifier : IErrorClassifier
{
    private readonly IReadOnlyList<IHttpFailureReader> _readers;

    /// <summary>Creates a classifier that reads HTTP failures with the built-in reader only.</summary>
    public DefaultErrorClassifier() : this(HttpFailureReaders.BuiltIn) { }

    /// <summary>Creates a classifier that reads HTTP failures with <paramref name="readers"/>, first answer wins.</summary>
    public DefaultErrorClassifier(IEnumerable<IHttpFailureReader> readers)
    {
        ArgumentNullException.ThrowIfNull(readers);
        _readers = readers.ToArray();
    }

    /// <summary>
    /// Classifies an exception for fallback/retry eligibility.
    /// Returns Terminal for unrecoverable errors (guard blocks, user cancellations),
    /// Retryable for transient network/timing issues (including HttpClient-timeout cancellations), classifies an HTTP
    /// failure by its status (see remarks), and returns FallbackEligible for others.
    /// Note: HttpClient request timeouts surface as TaskCanceledException with an inner TimeoutException — these are retryable.
    /// </summary>
    public ErrorClassification Classify(Exception ex)
    {
        switch (ex)
        {
            case GuardException:
                return ErrorClassification.Terminal;
            // HttpClient request timeout surfaces as TaskCanceledException with an inner TimeoutException -> retryable.
            case TaskCanceledException { InnerException: TimeoutException }:
                return ErrorClassification.Retryable;
            // Any other cancellation (user token) -> terminal, never retried or fallen back.
            case OperationCanceledException:
                return ErrorClassification.Terminal;
        }

        if (HttpFailureReaders.Read(_readers, ex) is { } failure)
            return ByStatus(failure);

        return ex switch
        {
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

    private static ErrorClassification ByStatus(HttpFailure failure) => failure.StatusCode switch
    {
        408 or 500 or 502 or 504 => ErrorClassification.Retryable,
        429 or 503 => failure.RetryAfter is null ? ErrorClassification.FallbackEligible : ErrorClassification.Retryable,
        _ => ErrorClassification.FallbackEligible
    };
}
