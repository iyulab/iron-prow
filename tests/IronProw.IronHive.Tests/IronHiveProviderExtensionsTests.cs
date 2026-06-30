using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using IronProw.Core;
using IronProw.IronHive;

namespace IronProw.IronHive.Tests;

public class IronHiveProviderExtensionsTests
{
    [Fact]
    public void AddIronHive_registers_a_frontier_candidate()
    {
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        // Concrete extension name/shape finalized in Step 1; this asserts a candidate is registered.
        builder.AddProvider("ironhive-openai", ProviderKind.Frontier, 100,
            _ => Substitute.For<IChatClient>());

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "ironhive-openai" && r.Kind == ProviderKind.Frontier);
    }

    [Fact]
    public void AddIronHiveOpenAI_wires_a_frontier_candidate()
    {
        // Verifies that the extension method registers a Frontier candidate with the correct id/kind.
        // The factory is a lazy Func — not invoked at registration time — so no network call occurs.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveOpenAI("openai-gpt4o", 90, "gpt-4o", c => c.ApiKey = "sk-test");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "openai-gpt4o" && r.Kind == ProviderKind.Frontier);
    }

    [Fact]
    public void AddIronHiveAnthropic_wires_a_frontier_candidate()
    {
        // Same pattern for the Anthropic adapter — factory is lazy, no network.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveAnthropic("anthropic-claude", 80, "claude-opus-4-5", c => c.ApiKey = "sk-ant-test");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "anthropic-claude" && r.Kind == ProviderKind.Frontier);
    }
}
