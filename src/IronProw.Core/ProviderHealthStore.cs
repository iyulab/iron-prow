using System.Collections.Concurrent;

namespace IronProw.Core;

/// <summary>
/// Singleton home of per-tenant <see cref="ProviderHealthTracker"/> instances. The per-tenant factory
/// (<see cref="IIronProwFactory"/>) is scoped and rebuilds a gateway on every call, so a tracker owned
/// by the gateway would forget everything at the end of each scope — which is no memory at all in a web
/// host. Keyed by tenant because provider ids are only unique within a tenant's registration set.
/// </summary>
internal sealed class ProviderHealthStore(ResilienceOptions options, TimeProvider time)
{
    private readonly ConcurrentDictionary<string, ProviderHealthTracker> _trackers = new(StringComparer.Ordinal);

    /// <summary>The tracker shared by every gateway built for <paramref name="tenant"/>.</summary>
    public ProviderHealthTracker ForTenant(string tenant)
        => _trackers.GetOrAdd(tenant, _ => new ProviderHealthTracker(options, time));
}
