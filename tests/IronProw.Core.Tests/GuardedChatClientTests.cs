#pragma warning disable CA2012
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

public class GuardedChatClientTests
{
    private static List<ChatMessage> Msgs() => [new(ChatRole.User, "hi")];

    [Fact]
    public async Task Blocks_input_before_calling_inner()
    {
        var inner = Substitute.For<IChatClient>();
        var guard = Substitute.For<IGuard>();
        guard.InspectInputAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GuardVerdict>(GuardVerdict.Block("injection")));

        var sut = new GuardedChatClient(inner, guard);

        var act = () => sut.GetResponseAsync(Msgs());
        (await act.Should().ThrowAsync<GuardException>()).Which.Reason.Should().Be("injection");
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Allows_passthrough_then_guards_output()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        var guard = Substitute.For<IGuard>();
        guard.InspectInputAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GuardVerdict>(GuardVerdict.Allow()));
        guard.InspectOutputAsync(Arg.Any<ChatResponse>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GuardVerdict>(GuardVerdict.Block("pii")));

        var sut = new GuardedChatClient(inner, guard);

        var act = () => sut.GetResponseAsync(Msgs());
        (await act.Should().ThrowAsync<GuardException>()).Which.Reason.Should().Be("pii");
    }
}
