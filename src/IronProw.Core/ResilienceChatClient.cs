using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// Retries a single provider on <see cref="ErrorClassification.Retryable"/> failures.
/// Non-retryable failures (fallback/terminal) propagate to the caller (or the selecting
/// orchestrator, which owns cross-provider fallback). Streaming retries only failures raised
/// before the first <see cref="ChatResponseUpdate"/> is yielded — once any chunk has been
/// emitted, a partially consumed stream cannot be safely retried, so the failure propagates.
/// </summary>
public sealed class ResilienceChatClient(
    IChatClient inner, IErrorClassifier classifier, ResilienceOptions options,
    Action<int, Exception>? onRetry = null) : DelegatingChatClient(inner)
{
    private readonly IErrorClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly ResilienceOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly Action<int, Exception>? _onRetry = onRetry;

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await base.GetResponseAsync(list, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                _classifier.Classify(ex) == ErrorClassification.Retryable && attempt < _options.MaxRetries)
            {
                _onRetry?.Invoke(attempt, ex);
                var delay = _options.BaseDelay * (attempt + 1);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        for (var attempt = 0; ; attempt++)
        {
            await using var enumerator = base.GetStreamingResponseAsync(list, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            var yielded = false;
            while (true)
            {
                ChatResponseUpdate? update;
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    update = hasNext ? enumerator.Current : null;
                }
                // Only failures before the first chunk are retryable — once yielded, a partial stream
                // cannot be restarted without double-emitting, so the exception propagates. When retries
                // are exhausted the filter is false and the exception surfaces to the selecting orchestrator.
                catch (Exception ex) when (
                    !yielded && _classifier.Classify(ex) == ErrorClassification.Retryable && attempt < _options.MaxRetries)
                {
                    _onRetry?.Invoke(attempt, ex);
                    var delay = _options.BaseDelay * (attempt + 1);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    break; // dispose this enumerator, then re-attempt from the top of the for loop
                }
                if (!hasNext)
                    yield break; // stream completed
                yielded = true;
                yield return update!;
            }
        }
    }
}
