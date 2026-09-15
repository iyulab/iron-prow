using AwesomeAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

/// <summary>
/// The gateway remembers provider health across calls. Measured on 0.3.49 with a dead LAN provider
/// ahead of a working local one: 15 of 16 calls paid retry ×2 plus fallback (~7 s each) because nothing
/// remembered the previous call's failures. After FailureThreshold consecutive degrading failures the
/// provider is demoted behind the healthy ones for Cooldown — demoted, not excluded — and every demotion
/// is reported as ProwTransitionKind.Skipped. A success clears the record; fallback disabled turns the
/// memory off for routing; a refused connection degrades immediately instead of being retried.
/// </summary>
public class ProviderHealthTests
{
    private sealed class AllowGuard : IGuard
    {
        public ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> m, CancellationToken ct) => new(GuardVerdict.Allow());
        public ValueTask<GuardVerdict> InspectOutputAsync(ChatResponse r, CancellationToken ct) => new(GuardVerdict.Allow());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static IChatClient Failing(string message = "down")
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new InvalidOperationException(message));
        return client;
    }

    private static IChatClient Working(string text = "ok")
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));
        return client;
    }

    private static (SelectingChatClient Sut, List<ProwTransition> Transitions, ManualTimeProvider Time) Build(
        IProviderRegistry registry, int failureThreshold = 2, bool enableFallback = true)
    {
        var transitions = new List<ProwTransition>();
        var time = new ManualTimeProvider();
        var options = new IronProwOptions
        {
            Resilience = new ResilienceOptions
            {
                MaxRetries = 0, BaseDelay = TimeSpan.Zero,
                FailureThreshold = failureThreshold, Cooldown = TimeSpan.FromSeconds(30)
            },
            EnableFallback = enableFallback,
            OnTransition = transitions.Add,
        };
        var sut = new SelectingChatClient(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
            new AllowGuard(), new DefaultErrorClassifier(), options, time);
        return (sut, transitions, time);
    }

    private static Task<ChatResponse> Call(SelectingChatClient sut)
        => sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task After_threshold_failures_the_provider_is_demoted_and_reported_as_skipped()
    {
        var dead = Failing();
        var alive = Working();
        var registry = new ProviderRegistry();
        registry.Register(new("dead-lan", ProviderKind.Lan, 100, _ => dead));
        registry.Register(new("local", ProviderKind.Local, 50, _ => alive));
        var (sut, transitions, _) = Build(registry, failureThreshold: 2);

        await Call(sut); // failure 1 -> fallback
        await Call(sut); // failure 2 -> threshold reached, cooldown armed
        transitions.Clear();
        await Call(sut); // demoted: local is tried first, dead-lan is not touched

        dead.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatClient.GetResponseAsync)).Should().Be(2,
            "the third call must not pay the dead provider's attempt at all");
        transitions.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = ProwTransitionKind.Skipped,
            ProviderId = "dead-lan",
            ProviderIndex = 0,
            TotalProviders = 2,
        });
    }

    [Fact]
    public async Task Cooldown_expires_and_the_provider_is_tried_again()
    {
        var dead = Failing();
        var alive = Working();
        var registry = new ProviderRegistry();
        registry.Register(new("dead-lan", ProviderKind.Lan, 100, _ => dead));
        registry.Register(new("local", ProviderKind.Local, 50, _ => alive));
        var (sut, transitions, time) = Build(registry, failureThreshold: 1);

        await Call(sut); // armed
        await Call(sut); // skipped
        time.Now += TimeSpan.FromSeconds(31);
        transitions.Clear();
        await Call(sut); // cooldown over: dead-lan is tried first again, fails, re-armed

        transitions.Select(t => t.Kind).Should().Equal(ProwTransitionKind.Fallback);
        transitions.Clear();
        await Call(sut);
        transitions.Select(t => t.Kind).Should().Equal(new[] { ProwTransitionKind.Skipped }, "the failure after expiry re-armed the cooldown");
    }

    [Fact]
    public async Task A_success_clears_the_failure_count()
    {
        var calls = 0;
        var flaky = Substitute.For<IChatClient>();
        flaky.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls % 2 == 0
                ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")))
                : throw new InvalidOperationException("flaky"));
        var registry = new ProviderRegistry();
        registry.Register(new("flaky", ProviderKind.Lan, 100, _ => flaky));
        registry.Register(new("local", ProviderKind.Local, 50, _ => Working()));
        var (sut, transitions, _) = Build(registry, failureThreshold: 2);

        for (var i = 0; i < 6; i++) await Call(sut); // fail, ok, fail, ok, ... never two in a row

        transitions.Should().NotContain(t => t.Kind == ProwTransitionKind.Skipped);
    }

    [Fact]
    public async Task All_providers_cooling_are_still_tried_in_order()
    {
        var a = Failing("a");
        var b = Failing("b");
        var registry = new ProviderRegistry();
        registry.Register(new("a", ProviderKind.Lan, 100, _ => a));
        registry.Register(new("b", ProviderKind.Local, 50, _ => b));
        var (sut, transitions, _) = Build(registry, failureThreshold: 1);

        await sut.Invoking(Call).Should().ThrowAsync<InvalidOperationException>(); // both armed
        transitions.Clear();
        (await sut.Invoking(Call).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("b");

        transitions.Select(t => t.Kind).Should().Equal(
            ProwTransitionKind.Skipped, ProwTransitionKind.Skipped, ProwTransitionKind.Fallback, ProwTransitionKind.Exhausted);
        a.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatClient.GetResponseAsync)).Should().Be(2,
            "demotion never removes a provider — with nobody healthy, it is still tried");
    }

    [Fact]
    public async Task Threshold_zero_disables_health_memory()
    {
        var dead = Failing();
        var registry = new ProviderRegistry();
        registry.Register(new("dead-lan", ProviderKind.Lan, 100, _ => dead));
        registry.Register(new("local", ProviderKind.Local, 50, _ => Working()));
        var (sut, transitions, _) = Build(registry, failureThreshold: 0);

        for (var i = 0; i < 4; i++) await Call(sut);

        dead.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatClient.GetResponseAsync)).Should().Be(4);
        transitions.Should().NotContain(t => t.Kind == ProwTransitionKind.Skipped);
    }

    [Fact]
    public async Task Fallback_disabled_never_routes_around_a_provider()
    {
        var dead = Failing();
        var registry = new ProviderRegistry();
        registry.Register(new("dead-lan", ProviderKind.Lan, 100, _ => dead));
        registry.Register(new("local", ProviderKind.Local, 50, _ => Working()));
        var (sut, transitions, _) = Build(registry, failureThreshold: 1, enableFallback: false);

        for (var i = 0; i < 3; i++)
            await sut.Invoking(Call).Should().ThrowAsync<InvalidOperationException>();

        dead.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatClient.GetResponseAsync)).Should().Be(3);
        transitions.Should().BeEmpty();
    }

    [Fact]
    public async Task Streaming_records_health_the_same_way()
    {
        var dead = Substitute.For<IChatClient>();
        dead.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Throwing());
        var alive = Substitute.For<IChatClient>();
        alive.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => One("ok"));
        var registry = new ProviderRegistry();
        registry.Register(new("dead-lan", ProviderKind.Lan, 100, _ => dead));
        registry.Register(new("local", ProviderKind.Local, 50, _ => alive));
        var (sut, transitions, _) = Build(registry, failureThreshold: 1);

        await Drain(sut);
        transitions.Clear();
        await Drain(sut);

        transitions.Select(t => t.Kind).Should().Equal(ProwTransitionKind.Skipped);
        dead.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatClient.GetStreamingResponseAsync)).Should().Be(1);

        static async IAsyncEnumerable<ChatResponseUpdate> Throwing()
        {
            await Task.Yield();
            throw new InvalidOperationException("down");
#pragma warning disable CS0162 // unreachable — needed to make this an iterator
            yield break;
#pragma warning restore CS0162
        }

        static async IAsyncEnumerable<ChatResponseUpdate> One(string text)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }

        static async Task Drain(SelectingChatClient sut)
        {
            await foreach (var _ in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken)) { }
        }
    }

    [Fact]
    public void Refused_connection_and_unresolvable_host_are_fallback_eligible_not_retryable()
    {
        var sut = new DefaultErrorClassifier();

        sut.Classify(new HttpRequestException(HttpRequestError.ConnectionError, "refused")).Should().Be(ErrorClassification.FallbackEligible);
        sut.Classify(new HttpRequestException(HttpRequestError.NameResolutionError, "no such host")).Should().Be(ErrorClassification.FallbackEligible);
        sut.Classify(new HttpRequestException(HttpRequestError.Unknown, "flaky")).Should().Be(ErrorClassification.Retryable, "an unclassified transport failure is still worth a retry");
        sut.Classify(new HttpRequestException("plain")).Should().Be(ErrorClassification.Retryable);
    }
}
