using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

public class ResilienceChatClientTests
{
    private static ResilienceOptions Fast() => new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero };

    [Fact]
    public async Task Retries_retryable_then_succeeds()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new HttpRequestException("1"), _ => Task.FromResult(response));

        var sut = new ResilienceChatClient(inner, new DefaultErrorClassifier(), Fast());
        var result = await sut.GetResponseAsync([new(ChatRole.User, "hi")]);

        result.Should().BeSameAs(response);
        await inner.Received(2).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rethrows_after_exhausting_retries()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new HttpRequestException("always"));

        var sut = new ResilienceChatClient(inner, new DefaultErrorClassifier(), Fast());
        await sut.Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<HttpRequestException>();
        // 1 initial + 2 retries = 3 calls
        await inner.Received(3).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_retry_terminal()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new GuardException("pii"));

        var sut = new ResilienceChatClient(inner, new DefaultErrorClassifier(), Fast());
        await sut.Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<GuardException>();
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    // --- Streaming: first-chunk resilience (mirror of the non-streaming retry contract) ---

    [Fact]
    public async Task Streaming_retries_retryable_before_first_chunk_then_succeeds()
    {
        var calls = 0;
        var inner = new FakeStreamClient(() => calls++ == 0
            ? Throwing(new HttpRequestException("first-chunk down"))   // attempt 0 fails at MoveNext
            : Yields("ok"));                                            // attempt 1 succeeds

        var sut = new ResilienceChatClient(inner, new DefaultErrorClassifier(), Fast());
        var text = await CollectAsync(sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")]));

        text.Should().Be("ok");
        inner.Calls.Should().Be(2); // initial + one retry
    }

    [Fact]
    public async Task Streaming_rethrows_after_exhausting_retries()
    {
        var inner = new FakeStreamClient(() => Throwing(new HttpRequestException("always")));

        var sut = new ResilienceChatClient(inner, new DefaultErrorClassifier(), Fast());
        await sut.Invoking(s => CollectAsync(s.GetStreamingResponseAsync([new(ChatRole.User, "hi")])))
            .Should().ThrowAsync<HttpRequestException>();
        inner.Calls.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Streaming_does_not_retry_after_first_chunk_yielded()
    {
        // Fails mid-stream (after a chunk was already emitted). Retrying would double-emit, so it must propagate.
        var inner = new FakeStreamClient(() => OneChunkThenThrow("partial", new HttpRequestException("mid-stream")));

        var sut = new ResilienceChatClient(inner, new DefaultErrorClassifier(), Fast());
        await sut.Invoking(s => CollectAsync(s.GetStreamingResponseAsync([new(ChatRole.User, "hi")])))
            .Should().ThrowAsync<HttpRequestException>();
        inner.Calls.Should().Be(1); // no retry once a chunk has been yielded
    }

    [Fact]
    public async Task Streaming_does_not_retry_terminal()
    {
        var inner = new FakeStreamClient(() => Throwing(new GuardException("pii")));

        var sut = new ResilienceChatClient(inner, new DefaultErrorClassifier(), Fast());
        await sut.Invoking(s => CollectAsync(s.GetStreamingResponseAsync([new(ChatRole.User, "hi")])))
            .Should().ThrowAsync<GuardException>();
        inner.Calls.Should().Be(1);
    }

    // --- streaming test helpers ---

    private sealed class FakeStreamClient(Func<IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClient
    {
        public int Calls { get; private set; }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return stream();
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static ChatResponseUpdate Update(string text)
        => new() { Role = ChatRole.Assistant, Contents = [new TextContent(text)] };

    private static async IAsyncEnumerable<ChatResponseUpdate> Throwing(Exception ex)
    {
        await Task.Yield();
        if (ex is not null) throw ex; // 'if' keeps the trailing yield reachable so this compiles as an iterator
        yield break;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Yields(params string[] texts)
    {
        await Task.Yield();
        foreach (var t in texts)
            yield return Update(t);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> OneChunkThenThrow(string text, Exception ex)
    {
        await Task.Yield();
        yield return Update(text);
        throw ex;
    }

    private static async Task<string> CollectAsync(IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var u in stream)
            sb.Append(u.Text);
        return sb.ToString();
    }
}
