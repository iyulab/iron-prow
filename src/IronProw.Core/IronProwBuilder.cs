using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IronProw.Core;

/// <summary>Fluent configuration for the iron-prow gateway.</summary>
public sealed class IronProwBuilder
{
    private readonly IServiceCollection _services;

    internal IronProwBuilder(IServiceCollection services) => _services = services;

    /// <summary>Registers an inference provider candidate.</summary>
    public IronProwBuilder AddProvider(string id, ProviderKind kind, int priority, Func<IServiceProvider, IChatClient> factory)
    {
        _services.AddSingleton(new ProviderRegistrationMarker(new ProviderRegistration(id, kind, priority, factory)));
        return this;
    }

    /// <summary>Replaces the guard implementation.</summary>
    public IronProwBuilder UseGuard(Func<IServiceProvider, IGuard> factory)
    {
        _services.AddSingleton(factory);
        return this;
    }

    /// <summary>Mutates gateway options.</summary>
    public IronProwBuilder Configure(Action<IronProwOptions> configure)
    {
        _services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Enables per-tenant provider resolution by registering an <see cref="IIronProwFactory"/> (scoped).
    /// The <paramref name="resolver"/> maps an opaque tenant key to that tenant's provider set, using the
    /// supplied (request-scoped) <see cref="IServiceProvider"/> to reach per-request services such as an
    /// already-decrypted workspace secret. Coexists with the single-tenant <see cref="AddProvider"/> path
    /// without regression. Async secret loading must be done upstream (see <see cref="IIronProwFactory"/>).
    /// </summary>
    public IronProwBuilder AddTenantResolver(
        Func<IServiceProvider, string, IReadOnlyList<ProviderRegistration>> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _services.TryAddScoped<IIronProwFactory>(sp => new IronProwFactory(
            sp,
            resolver,
            sp.GetRequiredService<IProviderSelector>(),
            sp.GetRequiredService<IGuard>(),
            sp.GetRequiredService<IErrorClassifier>(),
            sp.GetRequiredService<IOptions<IronProwOptions>>().Value));
        return this;
    }
}

/// <summary>DI marker carrying a provider registration (collected at registry build time).</summary>
public sealed record ProviderRegistrationMarker(ProviderRegistration Registration);
