namespace IronProw.Core;

/// <summary>Retry policy for a single provider attempt.</summary>
public sealed class ResilienceOptions
{
    /// <summary>Maximum retries after the initial attempt for <see cref="ErrorClassification.Retryable"/> failures.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base linear backoff; delay for attempt N is <c>BaseDelay * N</c>.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
}
