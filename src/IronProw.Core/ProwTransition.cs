namespace IronProw.Core;

/// <summary>Classifies a gateway transition reported via <see cref="IronProwOptions.OnTransition"/>.</summary>
public enum ProwTransitionKind
{
    /// <summary>A provider is being retried after a retryable failure (same provider).</summary>
    Retry,
    /// <summary>The gateway degraded from one provider to the next in priority order.</summary>
    Fallback,
    /// <summary>The last provider in the order failed; the gateway is about to surface the failure.</summary>
    Exhausted
}

/// <summary>
/// A structured gateway transition event (retry / fallback / exhausted), surfaced to consumers so they
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
    int Attempt = 0);
