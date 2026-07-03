using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using IronProw.Core;
using IronProw.IronHive;
using IronHive.Providers.OpenAI;

namespace IronProw.IronHive.Tests;

public class IronHiveProviderExtensionsTests
{
    [Fact]
    public void AddIronHive_registers_a_frontier_candidate()
    {
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        // Concrete extension name/shape finalized in Step 1; this asserts a candidate is registered.
        builder.AddProvider("ironhive-openai", ProviderKind.Frontier, 100,
            _ => Substitute.For<IChatClient>());

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "ironhive-openai" && r.Kind == ProviderKind.Frontier);
    }

    [Fact]
    public void AddIronHiveOpenAI_wires_a_frontier_candidate()
    {
        // Verifies that the extension method registers a Frontier candidate with the correct id/kind.
        // The factory is a lazy Func — not invoked at registration time — so no network call occurs.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveOpenAI("openai-gpt4o", 90, "gpt-4o", c => c.ApiKey = "sk-test");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "openai-gpt4o" && r.Kind == ProviderKind.Frontier);
    }

    [Fact]
    public void AddIronHiveAnthropic_wires_a_frontier_candidate()
    {
        // Same pattern for the Anthropic adapter — factory is lazy, no network.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveAnthropic("anthropic-claude", 80, "claude-opus-4-5", c => c.ApiKey = "sk-ant-test");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "anthropic-claude" && r.Kind == ProviderKind.Frontier);
    }

    [Fact]
    public void AddIronHiveGoogleAI_wires_a_frontier_candidate()
    {
        // GoogleAI is a cloud frontier provider — Frontier kind, lazy factory (no network at registration).
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveGoogleAI("google-gemini", 70, "gemini-2.5-pro", c => c.ApiKey = "test-key");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "google-gemini" && r.Kind == ProviderKind.Frontier);
    }

    [Fact]
    public void AddIronHiveGpuStack_wires_a_lan_candidate()
    {
        // GPUStack is a LAN provider — Lan kind. Key-optional, so configure sets only the base URL here.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveGpuStack("gpustack-local", 110, "qwen3", c => c.BaseUrl = "http://localhost:8080");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "gpustack-local" && r.Kind == ProviderKind.Lan);
    }

    [Fact]
    public void AddIronHiveOpenAICompatible_wires_a_lan_candidate()
    {
        // Generic OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, llama.cpp server) — LAN kind.
        // Key-optional and defaults to Ollama's http://localhost:11434, so configure may set only the model.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveOpenAICompatible("ollama-local", 105, "llama3.2", c => c.BaseUrl = "http://localhost:11434");

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "ollama-local" && r.Kind == ProviderKind.Lan);
    }

    [Fact]
    public void AddIronHiveOpenAICompatible_defaults_to_ollama_endpoint_when_unconfigured()
    {
        // Key-optional LAN default: no BaseUrl/ApiKey set. Registration must still succeed with a lazy
        // factory (no network at registration) — the default endpoint is Ollama's :11434.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveOpenAICompatible("ollama-default", 100, "qwen3", _ => { });

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Should().ContainSingle(r => r.Id == "ollama-default" && r.Kind == ProviderKind.Lan);
    }

    [Fact]
    public void Adapters_register_distinct_candidates_ordered_by_priority()
    {
        // The full Filer fallback set: gpustack(LAN) -> ollama(LAN) -> openai -> anthropic -> google.
        // LAN candidates rank highest; the registry orders by priority desc.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();

        builder.AddIronHiveGpuStack("gpustack", 110, "qwen3", c => c.BaseUrl = "http://localhost:8080");
        builder.AddIronHiveOpenAICompatible("ollama", 105, "llama3.2", c => c.BaseUrl = "http://localhost:11434");
        builder.AddIronHiveOpenAI("openai", 90, "gpt-4o", c => c.ApiKey = "sk-test");
        builder.AddIronHiveAnthropic("anthropic", 80, "claude-opus-4-5", c => c.ApiKey = "sk-ant-test");
        builder.AddIronHiveGoogleAI("google", 70, "gemini-2.5-pro", c => c.ApiKey = "test-key");

        var sp = services.BuildServiceProvider();
        var ordered = sp.GetRequiredService<IProviderRegistry>().GetOrdered();

        ordered.Select(r => r.Id).Should().Equal("gpustack", "ollama", "openai", "anthropic", "google");
        ordered[0].Kind.Should().Be(ProviderKind.Lan);
        ordered[1].Kind.Should().Be(ProviderKind.Lan);
    }

    [Fact]
    public void AddIronHiveOpenAI_defaults_to_ChatCompletions_surface()
    {
        // Guards a silent wire-protocol flip: ironhive 0.7.9's OpenAIConfig had no API-surface concept
        // (Chat Completions only); 0.8.2's OpenAIConfig.Api defaults to Responses. The Frontier adapter
        // must keep emitting Chat Completions unless the caller opts in. Captures the config the adapter
        // actually builds and asserts the default precedes the caller's configure callback.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();
        OpenAIConfig? captured = null;
        builder.AddIronHiveOpenAI("openai-frontier", 90, "gpt-4o", c =>
        {
            c.ApiKey = "sk-test";
            captured = c;
        });

        var sp = services.BuildServiceProvider();
        var reg = sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Single(r => r.Id == "openai-frontier");
        reg.ClientFactory(sp); // runs the factory: sets the default, then invokes configure (captures config)

        captured.Should().NotBeNull();
        captured!.Api.Should().Be(OpenAIApiSurface.ChatCompletions);
    }

    [Fact]
    public void AddIronHiveOpenAI_caller_can_override_surface_to_Responses()
    {
        // The ChatCompletions default is a default, not a lock — a caller targeting first-party OpenAI
        // reasoning can still opt into Responses.
        var services = new ServiceCollection();
        var builder = services.AddIronProw();
        OpenAIConfig? captured = null;
        builder.AddIronHiveOpenAI("openai-responses", 90, "gpt-4o", c =>
        {
            c.ApiKey = "sk-test";
            c.Api = OpenAIApiSurface.Responses;
            captured = c;
        });

        var sp = services.BuildServiceProvider();
        var reg = sp.GetRequiredService<IProviderRegistry>().GetOrdered()
            .Single(r => r.Id == "openai-responses");
        reg.ClientFactory(sp);

        captured!.Api.Should().Be(OpenAIApiSurface.Responses);
    }
}
