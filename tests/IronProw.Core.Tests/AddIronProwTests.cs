using FluentAssertions;
using IronProw.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

public class AddIronProwTests
{
    [Fact]
    public async Task Resolved_client_routes_through_registered_provider()
    {
        var ok = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var provider = Substitute.For<IChatClient>();
        provider.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ok));

        var services = new ServiceCollection();
        services.AddIronProw()
            .AddProvider("test", ProviderKind.Frontier, 100, _ => provider);
        var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<IChatClient>();
        var result = await client.GetResponseAsync([new(ChatRole.User, "hi")]);

        result.Should().BeSameAs(ok);
    }

    [Fact]
    public void UseGuard_override_wins_over_default_null_guard()
    {
        var custom = Substitute.For<IGuard>();

        var services = new ServiceCollection();
        services.AddIronProw().UseGuard(_ => custom);
        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IGuard>().Should().BeSameAs(custom);
    }
}
