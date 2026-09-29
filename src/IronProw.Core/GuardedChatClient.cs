using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// Decorates an <see cref="IChatClient"/> with input/output guardrails. Non-streaming calls inspect the input before the
/// call and the response after it. Streaming calls inspect the input before the call and the aggregated output when the
/// stream ends — or when the consumer stops reading early, as <see cref="DegenerationStopChatClient"/> does — and then
/// throw <see cref="GuardException"/> if it is blocked. Chunks already yielded stay yielded: the exception is the signal a
/// streaming consumer acts on (discard or retract what it showed).
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
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => new GuardedStream(this, messages as IReadOnlyList<ChatMessage> ?? messages.ToList(), options, cancellationToken);

    private IAsyncEnumerable<ChatResponseUpdate> InnerStream(
        IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
        => base.GetStreamingResponseAsync(messages, options, cancellationToken);

    /// <summary>
    /// The guarded stream: input inspected before the first update; the aggregated output inspected when the inner stream
    /// ends (thrown from <see cref="IAsyncEnumerator{T}.MoveNextAsync"/>) or, when the consumer stops reading early, on
    /// disposal. It is not inspected after the inner stream failed or the call was cancelled.
    /// </summary>
    private sealed class GuardedStream(
        GuardedChatClient owner, IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken callToken)
        : IAsyncEnumerable<ChatResponseUpdate>
    {
        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var token = cancellationToken.CanBeCanceled ? cancellationToken : callToken;
            return new Enumerator(owner, messages, options, token);
        }
    }

    private sealed class Enumerator(
        GuardedChatClient owner, IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
        : IAsyncEnumerator<ChatResponseUpdate>
    {
        private readonly List<ChatResponseUpdate> _updates = [];
        private IAsyncEnumerator<ChatResponseUpdate>? _inner;
        private bool _settled; // output inspected, or not to be inspected (inner failure)

        public ChatResponseUpdate Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_inner is null)
            {
                var input = await owner._guard.InspectInputAsync(messages, cancellationToken).ConfigureAwait(false);
                if (!input.Allowed)
                {
                    _settled = true;
                    throw new GuardException(input.Reason ?? "input blocked");
                }
                _inner = owner.InnerStream(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }

            bool more;
            try
            {
                more = await _inner.MoveNextAsync().ConfigureAwait(false);
            }
            catch
            {
                // The inner call failed: its exception is the caller's signal, not an output verdict.
                _settled = true;
                throw;
            }

            if (more)
            {
                Current = _inner.Current;
                _updates.Add(Current);
                return true;
            }

            await InspectOutputAsync().ConfigureAwait(false);
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            if (_inner is not null)
                await _inner.DisposeAsync().ConfigureAwait(false);

            // A consumer that stops reading early (a degeneration stop, or GetResponseAsync served through this stream)
            // must not keep output the guard never saw.
            await InspectOutputAsync().ConfigureAwait(false);
        }

        private async ValueTask InspectOutputAsync()
        {
            if (_settled || _updates.Count == 0 || cancellationToken.IsCancellationRequested)
                return;
            _settled = true;

            var output = await owner._guard.InspectOutputAsync(_updates.ToChatResponse(), CancellationToken.None).ConfigureAwait(false);
            if (!output.Allowed)
                throw new GuardException(output.Reason ?? "output blocked");
        }
    }
}
