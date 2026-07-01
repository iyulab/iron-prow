using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// The guarded gateway client. Selects a provider in priority order, wrapping each attempt with
/// guard + resilience, and degrades to the next provider on fallback-eligible failures.
/// </summary>
public sealed class SelectingChatClient(
    IServiceProvider services,
    IProviderRegistry registry,
    IProviderSelector selector,
    IGuard guard,
    IErrorClassifier classifier,
    IronProwOptions options) : IChatClient
{
    private readonly IronProwOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var order = selector.Order(new ChatSelectionContext(list, options, registry.GetOrdered()));
        if (order.Count == 0)
            throw new InvalidOperationException("No inference providers are registered.");

        Exception? last = null;
        for (var i = 0; i < order.Count; i++)
        {
            var reg = order[i];
            var attemptClient = BuildAttempt(reg, i, order.Count);
            try
            {
                return await attemptClient.GetResponseAsync(list, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var verdict = classifier.Classify(ex);
                if (verdict == ErrorClassification.Terminal)
                    throw;
                // Fallback disabled: never switch providers (a transient Retryable failure that exhausted
                // per-provider retries must not silently route to a different provider/ProviderKind).
                if (!_options.EnableFallback)
                    throw;
                last = ex; // try next candidate
                var isLast = i == order.Count - 1;
                Report(new ProwTransition(
                    isLast ? ProwTransitionKind.Exhausted : ProwTransitionKind.Fallback,
                    reg.Id, i, order.Count, verdict, ex));
            }
        }
        throw last ?? new InvalidOperationException("All providers failed.");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var order = selector.Order(new ChatSelectionContext(list, options, registry.GetOrdered()));
        if (order.Count == 0)
            throw new InvalidOperationException("No inference providers are registered.");

        // Mirror the non-streaming path: try providers in priority order, degrading on non-terminal failures.
        // The resilience window is "no ChatResponseUpdate yielded yet" — once a chunk is emitted, switching
        // providers would double-emit, so any later failure propagates. (Per-provider retry lives inside the
        // ResilienceChatClient the attempt is wrapped in, using the same before-first-chunk rule.)
        Exception? last = null;
        for (var i = 0; i < order.Count; i++)
        {
            var reg = order[i];
            var attemptClient = BuildAttempt(reg, i, order.Count);
            await using var enumerator = attemptClient.GetStreamingResponseAsync(list, options, cancellationToken)
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
                catch (Exception ex) when (!yielded)
                {
                    var verdict = classifier.Classify(ex);
                    if (verdict == ErrorClassification.Terminal)
                        throw;
                    // Fallback disabled: never switch providers (a transient Retryable failure that exhausted
                    // per-provider retries must not silently route to a different provider/ProviderKind).
                    if (!_options.EnableFallback)
                        throw;
                    last = ex; // try next candidate
                    var isLast = i == order.Count - 1;
                    Report(new ProwTransition(
                        isLast ? ProwTransitionKind.Exhausted : ProwTransitionKind.Fallback,
                        reg.Id, i, order.Count, verdict, ex));
                    break; // dispose this enumerator, then advance to the next provider
                }
                if (!hasNext)
                    yield break; // stream completed on this provider
                yielded = true;
                yield return update!;
            }
        }
        throw last ?? new InvalidOperationException("All providers failed.");
    }

    private ResilienceChatClient BuildAttempt(ProviderRegistration reg, int index, int total)
    {
        var raw = reg.ClientFactory(services);
        var guarded = new GuardedChatClient(raw, guard);
        // Only allocate a retry reporter when a consumer is listening — keeps the no-hook path allocation-free.
        Action<int, Exception>? onRetry = _options.OnTransition is null
            ? null
            : (attempt, ex) => Report(new ProwTransition(
                ProwTransitionKind.Retry, reg.Id, index, total, ErrorClassification.Retryable, ex, attempt));
        return new ResilienceChatClient(guarded, classifier, _options.Resilience, onRetry);
    }

    private void Report(ProwTransition transition)
    {
        var callback = _options.OnTransition;
        if (callback is null)
            return;
        try
        {
            callback(transition);
        }
        catch
        {
            // Best-effort telemetry: a consumer reporting callback must never break inference.
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(IChatClient) ? this : null;

    /// <inheritdoc />
    public void Dispose() => GC.SuppressFinalize(this);
}
