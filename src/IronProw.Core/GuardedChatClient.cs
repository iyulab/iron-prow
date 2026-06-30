using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// Decorates an <see cref="IChatClient"/> with input/output guardrails. Non-streaming calls
/// guard both directions. Streaming calls guard the input pre-flight only; output guarding of
/// a live stream is intentionally not applied (chunks are already emitted before a verdict).
/// </summary>
public sealed class GuardedChatClient(IChatClient inner, IGuard guard) : DelegatingChatClient(inner)
{
    private readonly IGuard _guard = guard ?? throw new ArgumentNullException(nameof(guard));

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var input = await _guard.InspectInputAsync(list, cancellationToken).ConfigureAwait(false);
        if (!input.Allowed)
            throw new GuardException(input.Reason ?? "input blocked");

        var response = await base.GetResponseAsync(list, options, cancellationToken).ConfigureAwait(false);

        var output = await _guard.InspectOutputAsync(response, cancellationToken).ConfigureAwait(false);
        if (!output.Allowed)
            throw new GuardException(output.Reason ?? "output blocked");

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var input = await _guard.InspectInputAsync(list, cancellationToken).ConfigureAwait(false);
        if (!input.Allowed)
            throw new GuardException(input.Reason ?? "input blocked");

        await foreach (var update in base.GetStreamingResponseAsync(list, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }
}
