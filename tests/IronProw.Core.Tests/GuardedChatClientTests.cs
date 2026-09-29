#pragma warning disable CA2012
using AwesomeAssertions;
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

    [Fact]
    public async Task Streaming_blocked_output_throws_when_the_stream_ends()
    {
        var guard = Guard(outputVerdict: GuardVerdict.Block("pii"));
        var sut = new GuardedChatClient(new StreamingStub("my card is ", "4111 1111"), guard);

        var seen = new List<string>();
        var act = async () =>
        {
            await foreach (var u in sut.GetStreamingResponseAsync(Msgs(), cancellationToken: TestContext.Current.CancellationToken))
                seen.Add(u.Text);
        };

        (await act.Should().ThrowAsync<GuardException>()).Which.Reason.Should().Be("pii");
        seen.Should().Equal("my card is ", "4111 1111");
        await guard.Received(1).InspectOutputAsync(
            Arg.Is<ChatResponse>(r => r.Text == "my card is 4111 1111"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Streaming_allowed_output_completes()
    {
        var guard = Guard(outputVerdict: GuardVerdict.Allow());
        var sut = new GuardedChatClient(new StreamingStub("fine ", "text"), guard);

        var text = string.Concat(await sut.GetStreamingResponseAsync(Msgs(), cancellationToken: TestContext.Current.CancellationToken).Select(u => u.Text).ToListAsync(TestContext.Current.CancellationToken));

        text.Should().Be("fine text");
        await guard.Received(1).InspectOutputAsync(Arg.Any<ChatResponse>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Degeneration_stop_around_the_guard_keeps_the_output_guard_for_non_streaming_calls()
    {
        // DegenerationStopChatClient serves GetResponseAsync through the inner stream; before, that skipped the output guard.
        var guard = Guard(outputVerdict: GuardVerdict.Block("pii"));
        var sut = new GuardedChatClient(new StreamingStub("my card is ", "4111 1111"), guard).WithDegenerationStop();

        var act = () => sut.GetResponseAsync(Msgs(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<GuardException>()).Which.Reason.Should().Be("pii");
    }

    [Fact]
    public async Task Degeneration_stop_that_abandons_the_stream_still_gets_the_output_inspected()
    {
        var guard = Guard(outputVerdict: GuardVerdict.Block("pii"));
        var looping = Enumerable.Repeat("again and again ", 200).ToArray();
        var sut = new GuardedChatClient(new StreamingStub(looping), guard).WithDegenerationStop();

        var act = () => sut.GetResponseAsync(Msgs(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<GuardException>()).Which.Reason.Should().Be("pii");
        await guard.Received(1).InspectOutputAsync(
            Arg.Is<ChatResponse>(r => r.Text.Length < looping.Length * looping[0].Length), Arg.Any<CancellationToken>());
    }

    private static IGuard Guard(GuardVerdict outputVerdict)
    {
        var guard = Substitute.For<IGuard>();
        guard.InspectInputAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GuardVerdict>(GuardVerdict.Allow()));
        guard.InspectOutputAsync(Arg.Any<ChatResponse>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GuardVerdict>(outputVerdict));
        return guard;
    }

    private sealed class StreamingStub(params string[] chunks) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("streaming only");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
