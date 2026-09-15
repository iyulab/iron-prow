using Microsoft.Extensions.AI;

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

    /// <summary>
    /// Reasoning effort injected when the caller leaves <c>ChatOptions.Reasoning</c> unset (the same
    /// "only when empty" rule as <see cref="DefaultMaxOutputTokens"/>). Defaults to
    /// <see cref="ReasoningEffort.None"/>: a bounded local call must spend its output budget on the
    /// answer — a thinking model left to its own default can exhaust <see cref="DefaultMaxOutputTokens"/>
    /// on reasoning and return an empty answer with <c>FinishReason == Length</c>. Set to
    /// <see langword="null"/> to inject nothing and keep each model's built-in default.
    /// </summary>
    public ReasoningEffort? DefaultReasoningEffort { get; set; } = ReasoningEffort.None;
}
