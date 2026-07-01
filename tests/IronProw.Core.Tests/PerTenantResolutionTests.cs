using FluentAssertions;
using IronProw.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

/// <summary>
/// Per-tenant provider resolution seam (VA-1). A multi-tenant consumer registers a resolver that
/// maps an opaque tenant key to that tenant's provider set; <see cref="IIronProwFactory.ForTenant"/>
/// rebuilds a guarded gateway over that per-tenant registry while reusing the shared
/// selector/guard/classifier/options.
/// </summary>
public class PerTenantResolutionTests
{
    private static IChatClient ProviderReturning(ChatResponse response)
    {
        var provider = Substitute.For<IChatClient>();
        provider.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        return provider;
    }

    [Fact]
    public async Task ForTenant_routes_to_tenant_specific_provider_set()
    {
        var respA = new ChatResponse(new ChatMessage(ChatRole.Assistant, "A"));
        var respB = new ChatResponse(new ChatMessage(ChatRole.Assistant, "B"));
        var provA = ProviderReturning(respA);
        var provB = ProviderReturning(respB);

        var services = new ServiceCollection();
        services.AddIronProw()
            .AddTenantResolver((_, tenant) => tenant switch
            {
                "A" => [new ProviderRegistration("a", ProviderKind.Frontier, 100, _ => provA)],
                "B" => [new ProviderRegistration("b", ProviderKind.Frontier, 100, _ => provB)],
                _ => []
            });
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IIronProwFactory>();

        var a = await factory.ForTenant("A").GetResponseAsync([new(ChatRole.User, "hi")]);
        var b = await factory.ForTenant("B").GetResponseAsync([new(ChatRole.User, "hi")]);

        a.Should().BeSameAs(respA);
        b.Should().BeSameAs(respB);
    }

    private sealed class TenantSecretHolder
    {
        public string Secret { get; set; } = "";
    }

    [Fact]
    public async Task ForTenant_resolves_registrations_through_request_scoped_service_provider()
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantSecretHolder>();
        services.AddIronProw()
            .AddTenantResolver((sp, tenant) =>
            {
                // The consumer's request middleware loads (async) and stashes the decrypted workspace
                // secret in a scoped service; the resolver reads it synchronously. iron-prow must hand
                // the *request scope's* provider here for that to work.
                var secret = sp.GetRequiredService<TenantSecretHolder>().Secret;
                return [new ProviderRegistration(tenant, ProviderKind.Frontier, 100,
                    _ => ProviderReturning(new ChatResponse(new ChatMessage(ChatRole.Assistant, secret))))];
            });
        var root = services.BuildServiceProvider();

        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantSecretHolder>().Secret = "ws-secret-42";
        var factory = scope.ServiceProvider.GetRequiredService<IIronProwFactory>();

        var resp = await factory.ForTenant("ws").GetResponseAsync([new(ChatRole.User, "hi")]);

        resp.Text.Should().Be("ws-secret-42");
    }

    private sealed class BlockingGuard : IGuard
    {
        public ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> m, CancellationToken ct)
            => new(GuardVerdict.Block("blocked"));
        public ValueTask<GuardVerdict> InspectOutputAsync(ChatResponse r, CancellationToken ct)
            => new(GuardVerdict.Allow());
    }

    [Fact]
    public async Task ForTenant_client_enforces_configured_guard()
    {
        var services = new ServiceCollection();
        services.AddIronProw()
            .UseGuard(_ => new BlockingGuard())
            .AddTenantResolver((_, _) =>
                [new ProviderRegistration("x", ProviderKind.Frontier, 100, _ => Substitute.For<IChatClient>())]);
        var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<IIronProwFactory>().ForTenant("t");

        await client.Invoking(c => c.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<GuardException>();
    }

    [Fact]
    public void ForTenant_throws_on_empty_tenant()
    {
        var services = new ServiceCollection();
        services.AddIronProw().AddTenantResolver((_, _) => []);
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IIronProwFactory>();

        factory.Invoking(f => f.ForTenant("")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IIronProwFactory_is_not_registered_without_AddTenantResolver()
    {
        var services = new ServiceCollection();
        services.AddIronProw().AddProvider("x", ProviderKind.Frontier, 100, _ => Substitute.For<IChatClient>());
        var sp = services.BuildServiceProvider();

        sp.GetService<IIronProwFactory>().Should().BeNull();
    }

    [Fact]
    public async Task Single_tenant_client_still_resolves_alongside_a_tenant_resolver()
    {
        // AC#2: the single-tenant IChatClient path must remain intact when a resolver is also present.
        var single = new ChatResponse(new ChatMessage(ChatRole.Assistant, "single"));
        var services = new ServiceCollection();
        services.AddIronProw()
            .AddProvider("single", ProviderKind.Frontier, 100, _ => ProviderReturning(single))
            .AddTenantResolver((_, _) =>
                [new ProviderRegistration("t", ProviderKind.Frontier, 100, _ => Substitute.For<IChatClient>())]);
        var sp = services.BuildServiceProvider();

        var resp = await sp.GetRequiredService<IChatClient>().GetResponseAsync([new(ChatRole.User, "hi")]);

        resp.Should().BeSameAs(single);
    }
}
