using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// The guarded gateway client. Selects a provider in priority order, wrapping each attempt with
/// guard + resilience, and degrades to the next provider on fallback-eligible failures. Remembers
/// provider health across calls: a provider that failed <see cref="ResilienceOptions.FailureThreshold"/>
/// times in a row is demoted behind the healthy ones for <see cref="ResilienceOptions.Cooldown"/>
/// (reported as <see cref="ProwTransitionKind.Skipped"/>), so a dead provider does not cost every call
/// its retry budget. Health memory is per gateway instance and only applies when fallback is enabled —
/// with fallback disabled the gateway never routes around a provider, not even a cooling one.
/// </summary>
public sealed class SelectingChatClient : IChatClient
{
    private readonly IServiceProvider services;
    private readonly IProviderRegistry registry;
    private readonly IProviderSelector selector;
    private readonly IGuard guard;
    private readonly IErrorClassifier classifier;
    private readonly IronProwOptions _options;
    private readonly ProviderHealthTracker _health;

    /// <summary>Creates a gateway that owns its own provider health memory.</summary>
    public SelectingChatClient(
        IServiceProvider services,
        IProviderRegistry registry,
        IProviderSelector selector,
        IGuard guard,
        IErrorClassifier classifier,
        IronProwOptions options,
        TimeProvider? timeProvider = null)
        : this(services, registry, selector, guard, classifier, options,
               new ProviderHealthTracker(Require(options).Resilience, timeProvider ?? TimeProvider.System))
    { }

    /// <summary>
    /// Creates a gateway over a shared health memory — the per-tenant factory hands every gateway it
    /// builds for a tenant the same tracker, so what one scope learned about a provider survives the scope.
    /// </summary>
    internal SelectingChatClient(
        IServiceProvider services,
        IProviderRegistry registry,
        IProviderSelector selector,
        IGuard guard,
        IErrorClassifier classifier,
        IronProwOptions options,
        ProviderHealthTracker health)
    {
        this.services = services;
        this.registry = registry;
        this.selector = selector;
        this.guard = guard;
        this.classifier = classifier;
        _options = Require(options);
        _health = health ?? throw new ArgumentNullException(nameof(health));
    }

    private static IronProwOptions Require(IronProwOptions options)
        => options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var order = Arrange(selector.Order(new ChatSelectionContext(list, options, registry.GetOrdered())));
        if (order.Count == 0)
            throw new InvalidOperationException("No inference providers are registered.");

        Exception? last = null;
        for (var i = 0; i < order.Count; i++)
        {
            var reg = order[i];
            var attemptClient = BuildAttempt(reg, i, order.Count);
            try
            {
                var response = await attemptClient.GetResponseAsync(list, options, cancellationToken).ConfigureAwait(false);
                _health.RecordSuccess(reg.Id);
                return response;
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
                _health.RecordFailure(reg.Id);
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
        var order = Arrange(selector.Order(new ChatSelectionContext(list, options, registry.GetOrdered())));
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
                    _health.RecordFailure(reg.Id);
                    last = ex; // try next candidate
                    var isLast = i == order.Count - 1;
                    Report(new ProwTransition(
                        isLast ? ProwTransitionKind.Exhausted : ProwTransitionKind.Fallback,
                        reg.Id, i, order.Count, verdict, ex));
                    break; // dispose this enumerator, then advance to the next provider
                }
                if (!hasNext)
                {
                    _health.RecordSuccess(reg.Id);
                    yield break; // stream completed on this provider
                }
                if (!yielded)
                {
                    // The first chunk proves the provider answered; a later mid-stream failure propagates
                    // to the caller and is not a routing signal.
                    _health.RecordSuccess(reg.Id);
                }
                yielded = true;
                yield return update!;
            }
        }
        throw last ?? new InvalidOperationException("All providers failed.");
    }

    /// <summary>
    /// Demotes cooling providers behind the healthy ones (never removes them) and reports each demotion
    /// as <see cref="ProwTransitionKind.Skipped"/>. A no-op with health memory off or fallback disabled.
    /// </summary>
    private IReadOnlyList<ProviderRegistration> Arrange(IReadOnlyList<ProviderRegistration> order)
    {
        if (!_health.Enabled || !_options.EnableFallback || order.Count < 2)
            return order;

        List<(ProviderRegistration Reg, int Index)>? cooling = null;
        for (var i = 0; i < order.Count; i++)
        {
            if (_health.IsCoolingDown(order[i].Id))
                (cooling ??= []).Add((order[i], i));
        }
        if (cooling is null)
            return order;

        var arranged = new List<ProviderRegistration>(order.Count);
        foreach (var reg in order)
        {
            if (!_health.IsCoolingDown(reg.Id))
                arranged.Add(reg);
        }
        foreach (var (reg, index) in cooling)
        {
            Report(new ProwTransition(ProwTransitionKind.Skipped, reg.Id, index, order.Count, ErrorClassification.FallbackEligible, null));
            arranged.Add(reg);
        }
        return arranged;
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
