using FluentAssertions;
using IronProw.Core;
using IronProw.LMSupply;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.LMSupply.Tests;

public class BuildLocalSafeClientTests
{
    [Fact]
    public void BuildLocalSafeClient_returns_a_guarded_local_chat_client_without_a_gateway()
    {
        // Lightweight path for single-local-provider consumers (textree): bridge + safety-wrap only,
        // no builder / registry / selection / resilience layers. The result is a plain IChatClient.
        var generator = Substitute.For<ITextGenerator>();
        var probe = Substitute.For<IReadinessProbe>();

        IChatClient client = LMSupplyExtensions.BuildLocalSafeClient(generator, probe);

        client.Should().NotBeNull();
        client.Should().BeOfType<LocalSafetyChatClient>(
            "the lightweight path wraps the GeneratorChatClient bridge in the safety decorator");
    }

    [Fact]
    public void BuildLocalSafeClient_honors_supplied_safety_options()
    {
        var generator = Substitute.For<ITextGenerator>();
        var probe = Substitute.For<IReadinessProbe>();
        var options = new LocalSafetyOptions { DefaultMaxOutputTokens = 256 };

        IChatClient client = LMSupplyExtensions.BuildLocalSafeClient(generator, probe, options);

        client.Should().BeOfType<LocalSafetyChatClient>();
    }

    [Fact]
    public void BuildLocalSafeClient_rejects_null_generator()
    {
        var probe = Substitute.For<IReadinessProbe>();

        var act = () => LMSupplyExtensions.BuildLocalSafeClient(null!, probe);

        act.Should().Throw<ArgumentNullException>().WithParameterName("generator");
    }

    [Fact]
    public void BuildLocalSafeClient_rejects_null_probe()
    {
        var generator = Substitute.For<ITextGenerator>();

        var act = () => LMSupplyExtensions.BuildLocalSafeClient(generator, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("probe");
    }
}
