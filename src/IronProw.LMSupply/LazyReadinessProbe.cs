namespace IronProw.LMSupply;

/// <summary>
/// An <see cref="IReadinessProbe"/> backed by a caller-supplied readiness delegate and a fixed model-id
/// set. For local providers that do <em>not</em> use a lm-supply generator pool — e.g. a single model
/// loaded directly via <c>LoadAsync</c> — where <see cref="GeneratorPoolProbe"/> does not apply.
/// </summary>
/// <remarks>
/// The <c>ready</c> delegate is evaluated on every probe, so it can reflect lazy/asynchronous load
/// completion (e.g. <c>() =&gt; generator.IsLoaded</c>). The model-id set is captured as a snapshot at
/// construction and reported as-is — appropriate when the model id is known at configuration time and
/// only readiness is deferred.
/// </remarks>
public sealed class LazyReadinessProbe : IReadinessProbe
{
    private readonly Func<bool> _ready;
    private readonly IReadOnlyList<string> _modelIds;

    /// <summary>Initializes a new <see cref="LazyReadinessProbe"/>.</summary>
    /// <param name="ready">Delegate evaluated on each probe; returns true when the provider can accept requests.</param>
    /// <param name="modelIds">The model ids available for inference (snapshotted at construction).</param>
    public LazyReadinessProbe(Func<bool> ready, IReadOnlyList<string> modelIds)
    {
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentNullException.ThrowIfNull(modelIds);
        _ready = ready;
        _modelIds = modelIds.ToArray(); // snapshot — the reported set must not change underneath the caller
    }

    /// <inheritdoc />
    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_ready());

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> GetAvailableModelIdsAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_modelIds);
}
