namespace IronProw.Core;

/// <summary>Top-level gateway behavior options.</summary>
public sealed class IronProwOptions
{
    /// <summary>Per-provider retry policy.</summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>When true, a fallback-eligible failure degrades to the next provider.</summary>
    public bool EnableFallback { get; set; } = true;
}
