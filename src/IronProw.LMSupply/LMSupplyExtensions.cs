using IronProw.Core;
using Microsoft.Extensions.AI;

namespace IronProw.LMSupply;

/// <summary>
/// Extension methods to register an lm-supply local inference provider as an iron-prow gateway candidate.
/// </summary>
public static class LMSupplyExtensions
{
    /// <summary>
    /// Registers a <see cref="ProviderKind.Local"/> candidate whose <see cref="IChatClient"/> is
    /// <paramref name="rawClient"/> wrapped in a <see cref="LocalSafetyChatClient"/>.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="rawClient">
    /// Pre-bridged <see cref="IChatClient"/> backed by lm-supply.
    /// lm-supply does not natively expose <see cref="IChatClient"/>; callers are responsible for
    /// constructing the bridge (e.g. via the adapter in <c>IronHive.Host.Core</c>) before passing
    /// it here. This avoids a layering violation between <c>IronProw.LMSupply</c> (Tier 2) and
    /// <c>ironhive-host</c> (Tier 3).
    /// </param>
    /// <param name="probe">Readiness and model-availability probe for the local provider.</param>
    /// <param name="options">Safety options; defaults are used when <see langword="null"/>.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IronProwBuilder AddLMSupplyLocal(
        this IronProwBuilder builder,
        string id,
        int priority,
        IChatClient rawClient,
        IReadinessProbe probe,
        LocalSafetyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(rawClient);
        ArgumentNullException.ThrowIfNull(probe);
        var safetyOptions = options ?? new LocalSafetyOptions();
        return builder.AddProvider(id, ProviderKind.Local, priority,
            _ => new LocalSafetyChatClient(rawClient, safetyOptions, probe));
    }
}
