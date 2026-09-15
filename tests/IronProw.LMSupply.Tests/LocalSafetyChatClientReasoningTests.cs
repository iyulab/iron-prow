#pragma warning disable CA2012 // ValueTask not directly awaited — NSubstitute .Returns() setup pattern
#pragma warning disable CA1861 // Array literal in .Returns() setup is intentional per-test, not repeated call site
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.LMSupply.Tests;

/// <summary>
/// The safety wrapper's fourth layer: a bounded local call spends its output budget on the answer.
/// LocalSafetyOptions.DefaultReasoningEffort (default None) is injected only where the caller left
/// ChatOptions.Reasoning unset — the same rule as DefaultMaxOutputTokens — and never into the caller's
/// own ChatOptions instance.
/// </summary>
public class LocalSafetyChatClientReasoningTests
{
    private static (IChatClient inner, IReadinessProbe probe, Func<ChatOptions?> captured) Harness()
    {
        ChatOptions? captured = null;
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions?>(o => captured = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        var probe = Substitute.For<IReadinessProbe>();
        probe.IsReadyAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(true));
        probe.GetAvailableModelIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<string>>(new[] { "gemma-4" }));
        return (inner, probe, () => captured);
    }

    [Fact]
    public async Task Default_injects_ReasoningEffort_None_when_caller_left_it_unset()
    {
        var (inner, probe, captured) = Harness();
        var sut = new LocalSafetyChatClient(inner, new LocalSafetyOptions(), probe);

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        captured()!.Reasoning.Should().NotBeNull();
        captured()!.Reasoning!.Effort.Should().Be(ReasoningEffort.None);
        captured()!.MaxOutputTokens.Should().Be(512, "the two defaults are injected together");
    }

    [Fact]
    public async Task Explicit_reasoning_is_not_overridden_and_the_caller_instance_is_not_mutated()
    {
        var (inner, probe, captured) = Harness();
        var sut = new LocalSafetyChatClient(inner, new LocalSafetyOptions(), probe);
        var callerOptions = new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High } };

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], callerOptions, TestContext.Current.CancellationToken);

        captured()!.Reasoning!.Effort.Should().Be(ReasoningEffort.High);
        callerOptions.MaxOutputTokens.Should().BeNull("the wrapper writes into a clone, never into the caller's options");
    }

    [Fact]
    public async Task Null_DefaultReasoningEffort_injects_nothing()
    {
        var (inner, probe, captured) = Harness();
        var sut = new LocalSafetyChatClient(inner, new LocalSafetyOptions { DefaultReasoningEffort = null }, probe);

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { MaxOutputTokens = 64 }, TestContext.Current.CancellationToken);

        captured()!.Reasoning.Should().BeNull();
    }
}
