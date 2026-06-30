namespace IronProw.LMSupply;

/// <summary>
/// Reports the readiness state and the set of available model IDs for a local inference provider.
/// </summary>
public interface IReadinessProbe
{
    /// <summary>
    /// Returns <see langword="true"/> when the local provider is ready to accept requests.
    /// </summary>
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the set of model IDs currently available for inference.
    /// Used by <see cref="LocalSafetyChatClient"/> to preflight the requested model.
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetAvailableModelIdsAsync(CancellationToken cancellationToken = default);
}
