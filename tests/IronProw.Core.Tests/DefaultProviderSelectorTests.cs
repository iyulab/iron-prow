using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

public class DefaultProviderSelectorTests
{
    [Fact]
    public void Order_returns_candidates_in_given_order()
    {
        var a = new ProviderRegistration("a", ProviderKind.Frontier, 100, _ => Substitute.For<IChatClient>());
        var b = new ProviderRegistration("b", ProviderKind.Lan, 50, _ => Substitute.For<IChatClient>());
        var ctx = new ChatSelectionContext([new(ChatRole.User, "hi")], null, [a, b]);

        new DefaultProviderSelector().Order(ctx).Select(r => r.Id).Should().ContainInOrder("a", "b");
    }
}
