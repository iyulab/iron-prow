using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public async Task Rethrows_terminal_immediately_without_trying_next()
    {
        var terminal = Substitute.For<IChatClient>();
        terminal.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new OperationCanceledException()); // classified Terminal
        var next = Substitute.For<IChatClient>();
        next.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unreached"))));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => terminal));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => next));

        await Build(registry).Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<OperationCanceledException>();
        await next.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rethrows_last_exception_when_all_providers_fail()
    {
        var first = Substitute.For<IChatClient>();
        first.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("first"));
        var second = Substitute.For<IChatClient>();
        second.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("second-last"));

        var registry = new ProviderRegistry();
        registry.Register(new("a", ProviderKind.Frontier, 100, _ => first));
        registry.Register(new("b", ProviderKind.Lan, 50, _ => second));

        (await Build(registry).Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("second-last");
    }

    [Fact]
    public async Task Does_not_fall_back_when_EnableFallback_is_false()
    {
        var failing = Substitute.For<IChatClient>();
        failing.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("down")); // FallbackEligible
        var backup = Substitute.For<IChatClient>();
        backup.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => failing));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => backup));

        var sut = new SelectingChatClient(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
            new AllowGuard(), new DefaultErrorClassifier(),
            new IronProwOptions { Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero }, EnableFallback = false });

        await sut.Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<InvalidOperationException>();
        await backup.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }
}
