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
        services.TryAddSingleton<IErrorClassifier, DefaultErrorClassifier>();
        services.TryAddSingleton<IGuard, NullGuard>();

        services.TryAddSingleton<IProviderRegistry>(sp =>
        {
            var registry = new ProviderRegistry();
            foreach (var marker in sp.GetServices<ProviderRegistrationMarker>())
                registry.Register(marker.Registration);
            return registry;
        });

        services.TryAddSingleton<IChatClient>(sp => new SelectingChatClient(
            sp,
            sp.GetRequiredService<IProviderRegistry>(),
            sp.GetRequiredService<IProviderSelector>(),
            sp.GetRequiredService<IGuard>(),
            sp.GetRequiredService<IErrorClassifier>(),
            sp.GetRequiredService<IOptions<IronProwOptions>>().Value));

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
