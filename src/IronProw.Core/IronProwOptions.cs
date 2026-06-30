namespace IronProw.Core;

/// <summary>Top-level gateway behavior options.</summary>
public sealed class IronProwOptions
{
    /// <summary>Per-provider retry policy.</summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// When false, only the highest-priority provider is ever attempted; a failure (after its own retries)
    /// is surfaced rather than degraded to another provider.
    /// When true (default), a fallback-eligible failure degrades to the next provider in priority order.
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// Optional best-effort callback invoked on each gateway transition (retry / fallback / exhausted), so
    /// consumers can surface resilience feedback (e.g. UI chips). Default null = no reporting. The callback
    /// runs synchronously on the inference path and must be cheap; any exception it throws is swallowed so
    /// reporting can never break inference.
    /// </summary>
    public Action<ProwTransition>? OnTransition { get; set; }
}
