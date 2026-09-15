namespace IronProw.Core;

/// <summary>Retry policy for a single provider attempt.</summary>
public sealed class ResilienceOptions
{
    /// <summary>Maximum retries after the initial attempt for <see cref="ErrorClassification.Retryable"/> failures.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base linear backoff; delay for attempt N is <c>BaseDelay * N</c>.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Consecutive degrading failures (retryable exhausted or fallback-eligible) after which a provider
    /// is put into cooldown and demoted behind the healthy providers for <see cref="Cooldown"/>. A cooling
    /// provider is still tried when no healthy one remains, and any success clears its record. Without this
    /// a dead provider costs every call its full retry budget before the gateway degrades past it.
    /// <c>0</c> disables health memory. Default 3.
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>How long a provider stays demoted after reaching <see cref="FailureThreshold"/>. Default 30 seconds.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(30);
}
