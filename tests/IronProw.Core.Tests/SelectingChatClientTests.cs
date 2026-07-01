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
    public async Task Does_not_switch_provider_on_exhausted_retryable_when_fallback_disabled()
    {
        var head = Substitute.For<IChatClient>();
        head.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new HttpRequestException("transient")); // Retryable
        var backup = Substitute.For<IChatClient>();
        backup.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unreached"))));

        var registry = new ProviderRegistry();
        registry.Register(new("head", ProviderKind.Lan, 100, _ => head));
        registry.Register(new("backup", ProviderKind.Frontier, 50, _ => backup));

        var sut = new SelectingChatClient(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
            new AllowGuard(), new DefaultErrorClassifier(),
            new IronProwOptions { Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero }, EnableFallback = false });

        await sut.Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<HttpRequestException>();
        await backup.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
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

    private static SelectingChatClient BuildWith(IProviderRegistry registry, IronProwOptions options)
        => new(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
               new AllowGuard(), new DefaultErrorClassifier(), options);

    [Fact]
    public async Task OnTransition_reports_fallback_then_success_with_index_and_total()
    {
        var ok = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var failing = Substitute.For<IChatClient>();
        failing.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("down")); // FallbackEligible
        var working = Substitute.For<IChatClient>();
        working.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ok));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => failing));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => working));

        var events = new List<ProwTransition>();
        var sut = BuildWith(registry, new IronProwOptions
        {
            Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero },
            OnTransition = events.Add
        });

        var result = await sut.GetResponseAsync([new(ChatRole.User, "hi")]);

        result.Should().BeSameAs(ok);
        // One Fallback event for the failed primary; the successful backup emits nothing.
        events.Should().ContainSingle();
        var e = events[0];
        e.Kind.Should().Be(ProwTransitionKind.Fallback);
        e.ProviderId.Should().Be("primary");
        e.ProviderIndex.Should().Be(0);
        e.TotalProviders.Should().Be(2);
        e.Category.Should().Be(ErrorClassification.FallbackEligible);
    }

    [Fact]
    public async Task OnTransition_reports_exhausted_when_last_provider_fails()
    {
        var first = Substitute.For<IChatClient>();
        first.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("first"));
        var second = Substitute.For<IChatClient>();
        second.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException("second"));

        var registry = new ProviderRegistry();
        registry.Register(new("a", ProviderKind.Frontier, 100, _ => first));
        registry.Register(new("b", ProviderKind.Lan, 50, _ => second));

        var events = new List<ProwTransition>();
        var sut = BuildWith(registry, new IronProwOptions
        {
            Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero },
            OnTransition = events.Add
        });

        await sut.Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<InvalidOperationException>();

        events.Select(e => e.Kind).Should().Equal(ProwTransitionKind.Fallback, ProwTransitionKind.Exhausted);
        events[^1].ProviderId.Should().Be("b");
        events[^1].ProviderIndex.Should().Be(1);
    }

    [Fact]
    public async Task OnTransition_reports_retry_attempts_before_fallback()
    {
        var flaky = Substitute.For<IChatClient>();
        flaky.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new HttpRequestException("transient")); // Retryable
        var ok = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var working = Substitute.For<IChatClient>();
        working.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ok));

        var registry = new ProviderRegistry();
        registry.Register(new("head", ProviderKind.Frontier, 100, _ => flaky));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => working));

        var events = new List<ProwTransition>();
        var sut = BuildWith(registry, new IronProwOptions
        {
            Resilience = new ResilienceOptions { MaxRetries = 2, BaseDelay = TimeSpan.Zero },
            OnTransition = events.Add
        });

        var result = await sut.GetResponseAsync([new(ChatRole.User, "hi")]);

        result.Should().BeSameAs(ok);
        // head: 2 retry attempts (attempt 0,1) then exhausts retries -> Retryable is not fallback-eligible at
        // the selector, but EnableFallback degrades on any non-terminal -> Fallback for head, then backup OK.
        events.Where(e => e.Kind == ProwTransitionKind.Retry).Select(e => e.Attempt).Should().Equal(0, 1);
        events.Should().Contain(e => e.Kind == ProwTransitionKind.Fallback && e.ProviderId == "head");
    }

    [Fact]
    public async Task OnTransition_callback_exception_does_not_break_inference()
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

        var sut = BuildWith(registry, new IronProwOptions
        {
            Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero },
            OnTransition = _ => throw new InvalidOperationException("callback boom")
        });

        // The throwing callback must be swallowed; the gateway still falls back and returns the working response.
        var result = await sut.GetResponseAsync([new(ChatRole.User, "hi")]);
        result.Should().BeSameAs(ok);
    }

    // --- Streaming: cross-provider fallback parity with the non-streaming path ---
    // The resilience window is "no ChatResponseUpdate yielded yet"; once a chunk is emitted, propagating is
    // correct (a mid-stream switch would double-emit). This mirrors GetResponseAsync for first-chunk failures.

    [Fact]
    public async Task Streaming_falls_back_to_next_provider_on_fallback_eligible()
    {
        var failing = StreamClient(() => Throwing(new InvalidOperationException("primary down"))); // FallbackEligible
        var working = StreamClient(() => Yields("ok:fallback"));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => failing));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => working));

        var text = await CollectAsync(Build(registry).GetStreamingResponseAsync([new(ChatRole.User, "hi")]));
        text.Should().Be("ok:fallback");
    }

    [Fact]
    public async Task Streaming_rethrows_terminal_without_trying_next()
    {
        var terminal = StreamClient(() => Throwing(new OperationCanceledException())); // Terminal
        var next = StreamClient(() => Yields("unreached"));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => terminal));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => next));

        await Build(registry).Invoking(s => CollectAsync(s.GetStreamingResponseAsync([new(ChatRole.User, "hi")])))
            .Should().ThrowAsync<OperationCanceledException>();
        next.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Streaming_does_not_fall_back_when_EnableFallback_is_false()
    {
        var failing = StreamClient(() => Throwing(new InvalidOperationException("down"))); // FallbackEligible
        var backup = StreamClient(() => Yields("unreached"));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => failing));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => backup));

        var sut = new SelectingChatClient(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
            new AllowGuard(), new DefaultErrorClassifier(),
            new IronProwOptions { Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero }, EnableFallback = false });

        await sut.Invoking(s => CollectAsync(s.GetStreamingResponseAsync([new(ChatRole.User, "hi")])))
            .Should().ThrowAsync<InvalidOperationException>();
        backup.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Streaming_does_not_fall_back_after_first_chunk_yielded()
    {
        // Primary emits a chunk, then fails mid-stream. Switching now would double-emit, so it must propagate.
        var primary = StreamClient(() => OneChunkThenThrow("partial", new InvalidOperationException("mid-stream")));
        var backup = StreamClient(() => Yields("unreached"));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => primary));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => backup));

        await Build(registry).Invoking(s => CollectAsync(s.GetStreamingResponseAsync([new(ChatRole.User, "hi")])))
            .Should().ThrowAsync<InvalidOperationException>();
        backup.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Streaming_OnTransition_reports_fallback_then_success()
    {
        var failing = StreamClient(() => Throwing(new InvalidOperationException("down"))); // FallbackEligible
        var working = StreamClient(() => Yields("ok"));

        var registry = new ProviderRegistry();
        registry.Register(new("primary", ProviderKind.Frontier, 100, _ => failing));
        registry.Register(new("backup", ProviderKind.Lan, 50, _ => working));

        var events = new List<ProwTransition>();
        var sut = BuildWith(registry, new IronProwOptions
        {
            Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero },
            OnTransition = events.Add
        });

        var text = await CollectAsync(sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")]));

        text.Should().Be("ok");
        events.Should().ContainSingle();
        events[0].Kind.Should().Be(ProwTransitionKind.Fallback);
        events[0].ProviderId.Should().Be("primary");
        events[0].ProviderIndex.Should().Be(0);
        events[0].TotalProviders.Should().Be(2);
        events[0].Category.Should().Be(ErrorClassification.FallbackEligible);
    }

    [Fact]
    public async Task Streaming_rethrows_last_exception_when_all_providers_fail()
    {
        var first = StreamClient(() => Throwing(new InvalidOperationException("first")));
        var second = StreamClient(() => Throwing(new InvalidOperationException("second-last")));

        var registry = new ProviderRegistry();
        registry.Register(new("a", ProviderKind.Frontier, 100, _ => first));
        registry.Register(new("b", ProviderKind.Lan, 50, _ => second));

        (await Build(registry).Invoking(s => CollectAsync(s.GetStreamingResponseAsync([new(ChatRole.User, "hi")])))
            .Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("second-last");
    }

    // --- streaming test helpers ---

    private sealed class StreamOnlyClient(Func<IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClient
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

    private static StreamOnlyClient StreamClient(Func<IAsyncEnumerable<ChatResponseUpdate>> stream) => new(stream);

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
