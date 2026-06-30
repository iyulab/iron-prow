#pragma warning disable CA1861 // Array literal in test setup is intentional per-test, not a repeated hot path
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.LMSupply.Tests;

public class LazyReadinessProbeTests
{
    [Fact]
    public async Task IsReadyAsync_re_evaluates_the_delegate_each_call()
    {
        var ready = false;
        var probe = new LazyReadinessProbe(() => ready, new[] { "gemma-3" });

        (await probe.IsReadyAsync()).Should().BeFalse();
        ready = true; // lazy load completed
        (await probe.IsReadyAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task GetAvailableModelIdsAsync_returns_the_configured_ids()
    {
        var probe = new LazyReadinessProbe(() => true, new[] { "gemma-3", "qwen3" });

        (await probe.GetAvailableModelIdsAsync()).Should().Equal("gemma-3", "qwen3");
    }

    [Fact]
    public async Task Model_ids_are_snapshotted_at_construction()
    {
        var source = new List<string> { "gemma-3" };
        var probe = new LazyReadinessProbe(() => true, source);

        source.Add("mutated-after-construction"); // must not leak into the probe

        (await probe.GetAvailableModelIdsAsync()).Should().Equal("gemma-3");
    }

    [Fact]
    public void Null_arguments_throw()
    {
        var act1 = () => new LazyReadinessProbe(null!, new[] { "m" });
        var act2 = () => new LazyReadinessProbe(() => true, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Works_as_preflight_probe_for_local_safety_client()
    {
        // The non-pool probe must satisfy LocalSafetyChatClient's preflight the same way GeneratorPoolProbe does.
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));

        var probe = new LazyReadinessProbe(() => true, new[] { "gemma-3" });
        var sut = new LocalSafetyChatClient(inner, new LocalSafetyOptions { DefaultMaxOutputTokens = 512 }, probe);

        var response = await sut.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { ModelId = "gemma-3" });
        response.Text.Should().Be("ok");
    }
}
