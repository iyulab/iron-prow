using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// Builds a guarded gateway <see cref="IChatClient"/> for a specific tenant. Enables multi-tenant
/// consumers (e.g. a web platform where each workspace has its own provider set and secrets) to
/// delegate selection/retry/fallback/guardrail while supplying providers per request.
/// </summary>
/// <remarks>
/// Registered (as scoped) only when <see cref="IronProwBuilder.AddTenantResolver"/> is configured.
/// Resolve it from the request scope so the resolver and provider factories see request-scoped
/// services. Async work (e.g. loading and decrypting a workspace secret from a database) must happen
/// upstream — in the consumer's request middleware, stashed into a scoped service — because both the
/// resolver and <see cref="ProviderRegistration.ClientFactory"/> are synchronous.
/// </remarks>
public interface IIronProwFactory
{
    /// <summary>
    /// Builds a gateway client over the provider set resolved for <paramref name="tenant"/>.
    /// The tenant key is opaque to iron-prow; the consumer's resolver interprets it.
    /// </summary>
    IChatClient ForTenant(string tenant);
}

/// <summary>
/// Default <see cref="IIronProwFactory"/>: rebuilds a <see cref="SelectingChatClient"/> over a
/// per-tenant <see cref="ProviderRegistry"/> while reusing the shared selector/guard/classifier/options.
/// </summary>
internal sealed class IronProwFactory(
    IServiceProvider services,
    Func<IServiceProvider, string, IReadOnlyList<ProviderRegistration>> resolver,
    IProviderSelector selector,
    IGuard guard,
    IErrorClassifier classifier,
    IronProwOptions options) : IIronProwFactory
{
    /// <inheritdoc />
    public IChatClient ForTenant(string tenant)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenant);

        var registry = new ProviderRegistry();
        foreach (var registration in resolver(services, tenant))
            registry.Register(registration);

        return new SelectingChatClient(services, registry, selector, guard, classifier, options);
    }
}
