using AwesomeAssertions;
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

        var a = await factory.ForTenant("A").GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);
        var b = await factory.ForTenant("B").GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

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

        var resp = await factory.ForTenant("ws").GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        resp.Text.Should().Be("ws-secret-42");
    }

    [Fact]
    public async Task ForTenant_health_memory_survives_the_scope_and_is_isolated_per_tenant()
    {
        // The factory is scoped and rebuilds the gateway per call; without a shared store a dead provider
        // would be re-learned by every request. Threshold 1: one failure arms the cooldown.
        var deadA = Substitute.For<IChatClient>();
        deadA.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("A is down"));
        var okA = ProviderReturning(new ChatResponse(new ChatMessage(ChatRole.Assistant, "A-backup")));
        var sameIdB = ProviderReturning(new ChatResponse(new ChatMessage(ChatRole.Assistant, "B-primary")));
        var transitions = new List<ProwTransition>();

        var services = new ServiceCollection();
        services.Configure<IronProwOptions>(o =>
        {
            o.Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero, FailureThreshold = 1 };
            o.OnTransition = transitions.Add;
        });
        services.AddIronProw()
            .AddTenantResolver((_, tenant) => tenant switch
            {
                "A" => [new ProviderRegistration("primary", ProviderKind.Lan, 100, _ => deadA),
                        new ProviderRegistration("backup", ProviderKind.Local, 50, _ => okA)],
                "B" => [new ProviderRegistration("primary", ProviderKind.Lan, 100, _ => sameIdB)],
                _ => []
            });
        var root = services.BuildServiceProvider();

        using (var scope1 = root.CreateScope())
        {
            await scope1.ServiceProvider.GetRequiredService<IIronProwFactory>().ForTenant("A")
                .GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);
        }
        transitions.Clear();
        using (var scope2 = root.CreateScope())
        {
            var factory = scope2.ServiceProvider.GetRequiredService<IIronProwFactory>();
            var a = await factory.ForTenant("A").GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);
            var b = await factory.ForTenant("B").GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

            a.Text.Should().Be("A-backup");
            b.Text.Should().Be("B-primary", "tenant B's provider shares the id 'primary' but not tenant A's failures");
        }

        transitions.Select(t => (t.Kind, t.ProviderId)).Should().Equal((ProwTransitionKind.Skipped, "primary"));
        deadA.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatClient.GetResponseAsync)).Should().Be(1,
            "the second scope's gateway must already know the provider is cooling down");
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

        var resp = await sp.GetRequiredService<IChatClient>().GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        resp.Should().BeSameAs(single);
    }
}
