using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IronProw.Core;

/// <summary>DI registration for the iron-prow safe-inference gateway.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the gateway and returns a builder for configuring providers and guards.</summary>
    public static IronProwBuilder AddIronProw(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IProviderSelector, DefaultProviderSelector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpFailureReader, HttpStatusFailureReader>());
        services.TryAddSingleton<IErrorClassifier>(sp => new DefaultErrorClassifier(sp.GetServices<IHttpFailureReader>()));
        services.TryAddSingleton<IGuard, NullGuard>();

        services.TryAddSingleton<IProviderRegistry>(sp =>
        {
            var registry = new ProviderRegistry();
            foreach (var marker in sp.GetServices<ProviderRegistrationMarker>())
                registry.Register(marker.Registration);
            return registry;
        });

        // Provider health memory outlives any scope: the single-tenant gateway is a singleton and owns
        // its own tracker; the per-tenant factory (scoped, rebuilt per call) borrows a tenant's tracker here.
        services.TryAddSingleton(sp => new ProviderHealthStore(
            sp.GetRequiredService<IOptions<IronProwOptions>>().Value.Resilience,
            sp.GetService<TimeProvider>() ?? TimeProvider.System));

        services.TryAddSingleton<IChatClient>(sp => new SelectingChatClient(
            sp,
            sp.GetRequiredService<IProviderRegistry>(),
            sp.GetRequiredService<IProviderSelector>(),
            sp.GetRequiredService<IGuard>(),
            sp.GetRequiredService<IErrorClassifier>(),
            sp.GetRequiredService<IOptions<IronProwOptions>>().Value,
            sp.GetService<TimeProvider>()));

        services.AddOptions<IronProwOptions>();
        return new IronProwBuilder(services);
    }
}

/// <summary>A guard that allows everything (used when no guard is configured).</summary>
internal sealed class NullGuard : IGuard
{
    public ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        => new(GuardVerdict.Allow());
    public ValueTask<GuardVerdict> InspectOutputAsync(ChatResponse response, CancellationToken ct)
        => new(GuardVerdict.Allow());
}
