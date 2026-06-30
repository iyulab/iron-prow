namespace IronProw.LMSupply;

/// <summary>
/// Configuration for <see cref="LocalSafetyChatClient"/> local-provider safety behaviour.
/// </summary>
public sealed class LocalSafetyOptions
{
    /// <summary>
    /// Maximum output tokens injected when the caller leaves <c>ChatOptions.MaxOutputTokens</c> unset.
    /// Prevents unbounded generation on constrained local hardware.
    /// </summary>
    public int DefaultMaxOutputTokens { get; set; } = 512;
}
