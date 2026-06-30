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
}
