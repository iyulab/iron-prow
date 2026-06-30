namespace IronProw.Core;

/// <summary>How the gateway should react to a failed inference attempt.</summary>
public enum ErrorClassification
{
    /// <summary>Transient — retry the same provider.</summary>
    Retryable,
    /// <summary>Provider-specific — degrade to the next provider.</summary>
    FallbackEligible,
    /// <summary>Non-recoverable — surface to the caller.</summary>
    Terminal
}
