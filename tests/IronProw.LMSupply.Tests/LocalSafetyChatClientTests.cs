#pragma warning disable CA2012 // ValueTask not directly awaited — NSubstitute .Returns() setup pattern
#pragma warning disable CA1861 // Array literal in .Returns() setup is intentional per-test, not repeated call site
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.LMSupply.Tests;

public class LocalSafetyChatClientTests
{
    [Fact]
    public async Task Preflight_throws_when_model_not_available()
    {
        var inner = Substitute.For<IChatClient>();
        var probe = Substitute.For<IReadinessProbe>();
        probe.IsReadyAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(true));
        probe.GetAvailableModelIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(new[] { "gemma-3" }));

        var sut = new LocalSafetyChatClient(inner, new LocalSafetyOptions { DefaultMaxOutputTokens = 512 }, probe);

        await sut.Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { ModelId = "missing" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await inner.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Length_bounding_injects_default_max_tokens()
    {
        var captured = (ChatOptions?)null;
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions?>(o => captured = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        var probe = Substitute.For<IReadinessProbe>();
        probe.IsReadyAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(true));
        probe.GetAvailableModelIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(new[] { "gemma-3" }));

        var sut = new LocalSafetyChatClient(inner, new LocalSafetyOptions { DefaultMaxOutputTokens = 512 }, probe);
        await sut.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { ModelId = "gemma-3" });

        captured!.MaxOutputTokens.Should().Be(512);
    }

    [Fact]
    public async Task Does_not_override_explicit_max_output_tokens()
    {
        var captured = (ChatOptions?)null;
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions?>(o => captured = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        var probe = Substitute.For<IReadinessProbe>();
        probe.IsReadyAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(true));
        probe.GetAvailableModelIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(new[] { "gemma-3" }));

        var sut = new LocalSafetyChatClient(inner, new LocalSafetyOptions { DefaultMaxOutputTokens = 512 }, probe);
        await sut.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { ModelId = "gemma-3", MaxOutputTokens = 4096 });

        captured!.MaxOutputTokens.Should().Be(4096);
    }
}
