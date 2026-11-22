namespace IronProw.Core;

/// <summary>Classifies a gateway transition reported via <see cref="IronProwOptions.OnTransition"/>.</summary>
public enum ProwTransitionKind
{
    /// <summary>A provider is being retried after a retryable failure (same provider).</summary>
    Retry,
    /// <summary>The gateway degraded from one provider to the next in priority order.</summary>
    Fallback,
    /// <summary>The last provider in the order failed; the gateway is about to surface the failure.</summary>
    Exhausted,
    /// <summary>
    /// A provider was passed over for this call because it is cooling down after
    /// <see cref="ResilienceOptions.FailureThreshold"/> consecutive failures (demoted behind the healthy
    /// providers; still tried when none of them succeeds).
    /// </summary>
    Skipped
}

/// <summary>
/// A structured gateway transition event (retry / fallback / exhausted / skipped), surfaced to consumers so they
/// can render resilience feedback (e.g. UI chips that show which provider is in use and why it switched).
/// Reporting is best-effort: a consumer callback that throws is swallowed and never breaks inference.
/// </summary>
/// <param name="Kind">The transition classification.</param>
/// <param name="ProviderId">Id of the provider the transition concerns (the one that failed / is being left).</param>
/// <param name="ProviderIndex">Zero-based index of <paramref name="ProviderId"/> in the selected order.</param>
/// <param name="TotalProviders">Total number of providers in the selected order.</param>
/// <param name="Category">The error classification that triggered the transition.</param>
/// <param name="Error">The failure that triggered the transition, if any.</param>
/// <param name="Attempt">For <see cref="ProwTransitionKind.Retry"/>, the zero-based retry attempt; otherwise 0.</param>
public sealed record ProwTransition(
    ProwTransitionKind Kind,
    string ProviderId,
    int ProviderIndex,
    int TotalProviders,
    ErrorClassification Category,
    Exception? Error,
    int Attempt = 0)
{
    /// <summary>
    /// The model name the request was sent to <see cref="ProviderId"/> with, or null when it was sent without one (the
    /// candidate's own default model - see <see cref="IronProwOptions.FallbackKeepsModelId"/>). Null for
    /// <see cref="ProwTransitionKind.Skipped"/>. The model that finally answered is on the response's
    /// <see cref="Microsoft.Extensions.AI.ChatResponse.ModelId"/>.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Why the provider refused, when the gateway can tell (see <see cref="ProwFailureReason"/>);
    /// <see cref="ProwFailureReason.Unknown"/> otherwise.
    /// </summary>
    public ProwFailureReason Reason { get; init; }
}

/// <summary>A reason the gateway can read from a provider's failure, for a UI to name.</summary>
public enum ProwFailureReason
{
    /// <summary>No reason the gateway recognizes.</summary>
    Unknown = 0,

    /// <summary>
    /// The server could not render the request into the model's prompt (a chat-template error - for example a strict
    /// template that refuses two messages of one role in a row). The same request meets the same refusal on that
    /// provider, so it is not retried there (see <see cref="DefaultErrorClassifier"/>).
    /// </summary>
    RequestNotRenderable = 1,
}
