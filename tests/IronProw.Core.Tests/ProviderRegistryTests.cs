using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

public class ProviderRegistryTests
{
    private static ProviderRegistration Reg(string id, int priority, ProviderKind kind = ProviderKind.Frontier)
        => new(id, kind, priority, _ => Substitute.For<IChatClient>());

    [Fact]
    public void GetOrdered_sorts_by_priority_descending()
    {
        var registry = new ProviderRegistry();
        registry.Register(Reg("low", 10));
        registry.Register(Reg("high", 100));
        registry.Register(Reg("mid", 50));

        registry.GetOrdered().Select(r => r.Id).Should().ContainInOrder("high", "mid", "low");
    }

    [Fact]
    public void Register_with_duplicate_id_replaces_previous()
    {
        var registry = new ProviderRegistry();
        registry.Register(Reg("p", 10));
        registry.Register(Reg("p", 99));

        registry.GetOrdered().Should().ContainSingle()
            .Which.Priority.Should().Be(99);
    }
}
