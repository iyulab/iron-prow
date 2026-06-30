using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

public class SelectingChatClientTests
{
    private sealed class AllowGuard : IGuard
    {
        public ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> m, CancellationToken ct) => new(GuardVerdict.Allow());
        public ValueTask<GuardVerdict> InspectOutputAsync(ChatResponse r, CancellationToken ct) => new(GuardVerdict.Allow());
    }

    private static SelectingChatClient Build(IProviderRegistry registry)
        => new(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
               new AllowGuard(), new DefaultErrorClassifier(),
               new IronProwOptions { Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero } });

    [Fact]
    public async Task Falls_back_to_next_provider_on_fallback_eligible()
    {
        var ok = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var failing = Substitute.For<IChatClient>();
        failing.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("down"));
        var working = Substitute.For<IChatClient>();
        working.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ok));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => failing));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => working));

        var result = await Build(registry).GetResponseAsync([new(ChatRole.User, "hi")]);
        result.Should().BeSameAs(ok);
    }

    [Fact]
    public async Task Throws_when_no_candidates()
    {
        await Build(new ProviderRegistry())
            .Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
