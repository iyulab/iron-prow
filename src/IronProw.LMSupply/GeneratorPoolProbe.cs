using LMSupply.Generator;

namespace IronProw.LMSupply;

/// <summary>
/// An <see cref="IReadinessProbe"/> backed by a lm-supply <see cref="GeneratorPool"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Readiness:</b> reports ready when at least one model is loaded
/// (<see cref="GeneratorPool.LoadedModelCount"/> &gt; 0). lm-supply exposes no explicit
/// "provider ready" signal; loaded-model count is the closest available approximation.
/// </para>
/// <para>
/// <b>Available models:</b> maps to the set of currently <em>loaded</em> model IDs
/// returned by <see cref="GeneratorPool.GetLoadedModels"/>. There is no global catalog API;
/// only models that have already been loaded via <c>GeneratorPool.GetOrLoadAsync</c> appear here.
/// </para>
/// </remarks>
public sealed class GeneratorPoolProbe(GeneratorPool pool) : IReadinessProbe
{
    private readonly GeneratorPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));

    /// <inheritdoc />
    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_pool.LoadedModelCount > 0);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> GetAvailableModelIdsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> ids = _pool.GetLoadedModels().Select(m => m.ModelId).ToList();
        return ValueTask.FromResult(ids);
    }
}
