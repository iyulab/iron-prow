using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// Retries a single provider on <see cref="ErrorClassification.Retryable"/> failures.
/// Non-retryable failures (fallback/terminal) propagate to the caller (or the selecting
/// orchestrator, which owns cross-provider fallback). Streaming is passthrough — a partially
/// consumed stream cannot be safely retried.
/// </summary>
public sealed class ResilienceChatClient(
    IChatClient inner, IErrorClassifier classifier, ResilienceOptions options) : DelegatingChatClient(inner)
{
    private readonly IErrorClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly ResilienceOptions _options = options ?? throw new ArgumentNullException(nameof(options));

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
                var delay = _options.BaseDelay * (attempt + 1);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
