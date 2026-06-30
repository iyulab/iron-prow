using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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
}

/// <summary>DI marker carrying a provider registration (collected at registry build time).</summary>
public sealed record ProviderRegistrationMarker(ProviderRegistration Registration);
