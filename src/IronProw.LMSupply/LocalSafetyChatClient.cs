using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace IronProw.LMSupply;

/// <summary>
/// Decorates a local-provider <see cref="IChatClient"/> with three safety layers applied in order:
/// <list type="number">
///   <item><term>Readiness gate</term><description>Throws <see cref="InvalidOperationException"/> if <see cref="IReadinessProbe.IsReadyAsync"/> returns <see langword="false"/>.</description></item>
///   <item><term>Model-ID preflight</term><description>Throws <see cref="InvalidOperationException"/> when <c>ChatOptions.ModelId</c> is set but not in the set reported by <see cref="IReadinessProbe.GetAvailableModelIdsAsync"/>.</description></item>
///   <item><term>Length-bounding</term><description>Injects <see cref="LocalSafetyOptions.DefaultMaxOutputTokens"/> when the caller leaves <c>ChatOptions.MaxOutputTokens</c> unset, preventing unbounded generation on constrained local hardware.</description></item>
///   <item><term>Reasoning default</term><description>Injects <see cref="LocalSafetyOptions.DefaultReasoningEffort"/> when the caller leaves <c>ChatOptions.Reasoning</c> unset, so the bounded budget goes to the answer rather than to a thinking model's reasoning.</description></item>
/// </list>
/// <para>
/// <b>Crash-fallback limitation:</b> True ONNX GenAI DirectML inference-crash CPU-fallback is an
/// upstream lm-supply concern (tracked in
/// <c>ISSUE-LMSupply.Generator-20260629-onnx-directml-inference-crash-no-cpu-fallback.md</c>,
/// owner-gated). <c>LocalSafetyChatClient</c> provides gateway-level safety only: a pre-first-token
/// local crash surfaces as an unhandled exception that <c>SelectingChatClient</c> can classify as
/// <c>FallbackEligible</c> and use to degrade to the next configured provider.
/// Mid-stream provider swaps are intentionally not attempted.
/// </para>
/// </summary>
public sealed class LocalSafetyChatClient(IChatClient inner, LocalSafetyOptions options, IReadinessProbe probe)
    : DelegatingChatClient(inner)
{
    private readonly LocalSafetyOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IReadinessProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = await ApplySafetyAsync(options, cancellationToken).ConfigureAwait(false);
        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = await ApplySafetyAsync(options, cancellationToken).ConfigureAwait(false);
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    private async ValueTask<ChatOptions?> ApplySafetyAsync(ChatOptions? options, CancellationToken cancellationToken)
    {
        if (!await _probe.IsReadyAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Local model provider is not ready.");

        if (options?.ModelId is { } modelId)
        {
            var available = await _probe.GetAvailableModelIdsAsync(cancellationToken).ConfigureAwait(false);
            if (!available.Contains(modelId))
                throw new InvalidOperationException(
                    $"Model '{modelId}' is not available locally. Available: {string.Join(", ", available)}");
        }

        // Both defaults apply only where the caller left the slot empty, and both write into a clone so the
        // caller's ChatOptions instance is never mutated.
        var injectMaxOutputTokens = options?.MaxOutputTokens is null;
        var injectReasoning = _options.DefaultReasoningEffort is not null && options?.Reasoning is null;
        if (injectMaxOutputTokens || injectReasoning)
        {
            options = options?.Clone() ?? new ChatOptions();
            if (injectMaxOutputTokens)
            {
                options.MaxOutputTokens = _options.DefaultMaxOutputTokens;
            }
            if (injectReasoning)
            {
                options.Reasoning = new ReasoningOptions { Effort = _options.DefaultReasoningEffort };
            }
        }

        return options;
    }
}
