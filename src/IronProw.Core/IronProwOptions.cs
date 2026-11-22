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
    /// When false (default), a request that falls back to a candidate other than the one it was meant for (the first
    /// in the selector's order) is sent without its <see cref="Microsoft.Extensions.AI.ChatOptions.ModelId"/>, unless
    /// that candidate lists the name in <see cref="ProviderRegistration.ServedModels"/>: a model name belongs to the
    /// provider it names, and another provider refuses it («unknown model», «model has no price»), so the fallback
    /// could not succeed. The candidate's client then uses its own default model. When true, every candidate is sent
    /// the request's model name as is.
    /// </summary>
    public bool FallbackKeepsModelId { get; set; }

    /// <summary>
    /// Optional best-effort callback invoked on each gateway transition (retry / fallback / exhausted), so
    /// consumers can surface resilience feedback (e.g. UI chips). Default null = no reporting. The callback
    /// runs synchronously on the inference path and must be cheap; any exception it throws is swallowed so
    /// reporting can never break inference.
    /// </summary>
    public Action<ProwTransition>? OnTransition { get; set; }
}
