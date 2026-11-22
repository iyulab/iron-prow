using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

// A request's ModelId names the model of the candidate it was meant for; a fallback onto another candidate must not
// carry it (that provider refuses the name), and a server that cannot render the request into its prompt is not retried.
public class FallbackModelAndRenderRefusalTests
{
    private sealed class AllowGuard : IGuard
    {
        public ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> m, CancellationToken ct) => new(GuardVerdict.Allow());
        public ValueTask<GuardVerdict> InspectOutputAsync(ChatResponse r, CancellationToken ct) => new(GuardVerdict.Allow());
    }

    /// <summary>A provider that records the ModelId of every request and fails with <paramref name="failure"/> when given one.</summary>
    private sealed class RecordingClient(Exception? failure) : IChatClient
    {
        public List<string?> ModelIds { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ModelIds.Add(options?.ModelId);
            if (failure is not null)
                throw failure;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ModelIds.Add(options?.ModelId);
            await Task.Yield();
            if (failure is not null)
                throw failure;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static HttpRequestException Http(HttpStatusCode status, string message) => new(message, null, status);

    private static SelectingChatClient Gateway(ProviderRegistry registry, IronProwOptions? options = null, int maxRetries = 0)
    {
        options ??= new IronProwOptions();
        options.Resilience = new ResilienceOptions { MaxRetries = maxRetries, BaseDelay = TimeSpan.Zero };
        return new SelectingChatClient(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
            new AllowGuard(), new DefaultErrorClassifier(), options);
    }

    private static (RecordingClient First, RecordingClient Second, ProviderRegistry Registry) TwoCandidates(
        IReadOnlyCollection<string>? secondServes = null)
    {
        var first = new RecordingClient(Http(HttpStatusCode.BadRequest, "first refuses"));
        var second = new RecordingClient(null);
        var registry = new ProviderRegistry();
        registry.Register(new("openai", ProviderKind.Frontier, 100, _ => first));
        registry.Register(new("gpustack", ProviderKind.Lan, 50, _ => second) { ServedModels = secondServes });
        return (first, second, registry);
    }

    private static readonly ChatOptions Named = new() { ModelId = "anthropic/claude-sonnet-5.5", Temperature = 0.2f };

    [Fact]
    public async Task A_fallback_candidate_is_sent_the_request_without_the_first_candidates_model()
    {
        var (first, second, registry) = TwoCandidates();

        await Gateway(registry).GetResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken);

        first.ModelIds.Should().Equal("anthropic/claude-sonnet-5.5");
        second.ModelIds.Should().Equal([null], "the candidate's own default model answers");
        Named.ModelId.Should().Be("anthropic/claude-sonnet-5.5", "the caller's options are not changed");
    }

    [Fact]
    public async Task A_streamed_fallback_is_sent_without_it_too()
    {
        var (first, second, registry) = TwoCandidates();

        await foreach (var _ in Gateway(registry).GetStreamingResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken))
        {
        }

        first.ModelIds.Should().Equal("anthropic/claude-sonnet-5.5");
        second.ModelIds.Should().Equal([null]);
    }

    [Fact]
    public async Task A_candidate_that_serves_the_name_keeps_it()
    {
        var (_, second, registry) = TwoCandidates(secondServes: ["anthropic/claude-sonnet-5.5"]);

        await Gateway(registry).GetResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken);

        second.ModelIds.Should().Equal("anthropic/claude-sonnet-5.5");
    }

    [Fact]
    public async Task ServeModels_declares_names_for_a_candidate_an_adapter_registered()
    {
        var first = new RecordingClient(Http(HttpStatusCode.BadRequest, "first refuses"));
        var second = new RecordingClient(null);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddIronProw()
            .AddProvider("openai", ProviderKind.Frontier, 100, _ => first)
            .AddProvider("gpustack", ProviderKind.Lan, 50, _ => second)
            .ServeModels("gpustack", "qwen3-8b")
            .ServeModels("gpustack", "anthropic/claude-sonnet-5.5", "qwen3-8b")
            .Configure(o => o.Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero });
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

        var registry = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IProviderRegistry>(provider);
        registry.GetOrdered().Single(r => r.Id == "gpustack").ServedModels.Should().BeEquivalentTo(["qwen3-8b", "anthropic/claude-sonnet-5.5"]);
        registry.GetOrdered().Single(r => r.Id == "openai").ServedModels.Should().BeNull();

        var gateway = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IChatClient>(provider);
        await gateway.GetResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken);

        second.ModelIds.Should().Equal("anthropic/claude-sonnet-5.5");
    }

    [Fact]
    public async Task FallbackKeepsModelId_sends_the_name_to_every_candidate()
    {
        var (_, second, registry) = TwoCandidates();

        await Gateway(registry, new IronProwOptions { FallbackKeepsModelId = true })
            .GetResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken);

        second.ModelIds.Should().Equal("anthropic/claude-sonnet-5.5");
    }

    [Fact]
    public async Task The_transitions_say_which_model_each_candidate_was_sent()
    {
        var first = new RecordingClient(Http(HttpStatusCode.BadRequest, "first refuses"));
        var second = new RecordingClient(Http(HttpStatusCode.BadRequest, "second refuses"));
        var registry = new ProviderRegistry();
        registry.Register(new("openai", ProviderKind.Frontier, 100, _ => first));
        registry.Register(new("gpustack", ProviderKind.Lan, 50, _ => second));
        var seen = new List<ProwTransition>();

        await Gateway(registry, new IronProwOptions { OnTransition = seen.Add })
            .Invoking(g => g.GetResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<HttpRequestException>();

        seen.Select(t => (t.Kind, t.ProviderId, t.ModelId)).Should().Equal(
            (ProwTransitionKind.Fallback, "openai", "anthropic/claude-sonnet-5.5"),
            (ProwTransitionKind.Exhausted, "gpustack", (string?)null));
    }

    [Fact]
    public async Task A_demoted_target_does_not_make_the_next_candidate_the_target()
    {
        // The selector picked "openai" for this request; a health demotion that tries "gpustack" first must still send
        // gpustack the request without openai's model.
        var flaky = new RecordingClient(Http(HttpStatusCode.BadRequest, "first refuses"));
        var second = new RecordingClient(null);
        var registry = new ProviderRegistry();
        registry.Register(new("openai", ProviderKind.Frontier, 100, _ => flaky));
        registry.Register(new("gpustack", ProviderKind.Lan, 50, _ => second));
        var seen = new List<ProwTransition>();
        var options = new IronProwOptions
        {
            Resilience = new ResilienceOptions { MaxRetries = 0, BaseDelay = TimeSpan.Zero, FailureThreshold = 1 },
            OnTransition = seen.Add,
        };
        var gateway = new SelectingChatClient(Substitute.For<IServiceProvider>(), registry, new DefaultProviderSelector(),
            new AllowGuard(), new DefaultErrorClassifier(), options);

        // First call: openai fails and enters its cooldown; second call: gpustack is tried first.
        await gateway.GetResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken);
        await gateway.GetResponseAsync([new(ChatRole.User, "hi")], Named, TestContext.Current.CancellationToken);

        seen.Should().Contain(t => t.Kind == ProwTransitionKind.Skipped && t.ProviderId == "openai",
            "positive control: the second call must run with openai demoted");
        flaky.ModelIds.Should().HaveCount(1, "the demoted openai is not reached once gpustack answers");
        second.ModelIds.Should().Equal([null, null]);
    }

    public static TheoryData<string> RenderRefusals => new()
    {
        // minja (llama.cpp's Jinja engine), as GPUStack relays it.
        "Engine protocol predict request returned 500: While executing CallExpression at line 46, column 21 in source",
        // A template calling its raise helper.
        "Error: raise_exception('Conversation roles must alternate user/assistant/user/assistant/...')",
        // Python jinja2 (vLLM).
        "jinja2.exceptions.TemplateError: Conversation roles must alternate user/assistant/user/assistant/...",
        "TemplateSyntaxError: unexpected '}'",
        // Servers that name the step.
        "Failed to apply chat template",
        "Error in applying chat template from request",
        // Go text/template (Ollama).
        "template: :12:5: executing \"\" at <.Messages>: error calling index: index out of range",
    };

    [Theory]
    [MemberData(nameof(RenderRefusals))]
    public void A_500_that_is_a_chat_template_refusal_is_not_retried(string body)
    {
        new DefaultErrorClassifier().Classify(Http(HttpStatusCode.InternalServerError, body))
            .Should().Be(ErrorClassification.FallbackEligible);
    }

    [Theory]
    [InlineData("Internal Server Error")]
    [InlineData("upstream connect error or disconnect/reset before headers")]
    [InlineData("the model template cache was busy, try again")]
    public void A_500_without_a_template_signature_stays_retryable(string body)
    {
        new DefaultErrorClassifier().Classify(Http(HttpStatusCode.InternalServerError, body))
            .Should().Be(ErrorClassification.Retryable);
    }

    [Fact]
    public async Task A_render_refusal_is_tried_once_and_reported_with_its_reason()
    {
        var refusing = new RecordingClient(Http(HttpStatusCode.InternalServerError,
            "Engine protocol predict request returned 500: While executing CallExpression at line 46, column 21"));
        var backup = new RecordingClient(null);
        var registry = new ProviderRegistry();
        registry.Register(new("gpustack", ProviderKind.Lan, 100, _ => refusing));
        registry.Register(new("lmsupply", ProviderKind.Local, 50, _ => backup));
        var seen = new List<ProwTransition>();

        await Gateway(registry, new IronProwOptions { OnTransition = seen.Add }, maxRetries: 3)
            .GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        refusing.ModelIds.Should().HaveCount(1, "the same request meets the same refusal - no retry on that provider");
        backup.ModelIds.Should().HaveCount(1);
        seen.Should().ContainSingle().Which.Should().Match<ProwTransition>(t =>
            t.Kind == ProwTransitionKind.Fallback && t.Reason == ProwFailureReason.RequestNotRenderable);
    }

    [Fact]
    public async Task A_plain_500_is_retried_and_reports_no_reason()
    {
        var failing = new RecordingClient(Http(HttpStatusCode.InternalServerError, "Internal Server Error"));
        var backup = new RecordingClient(null);
        var registry = new ProviderRegistry();
        registry.Register(new("gpustack", ProviderKind.Lan, 100, _ => failing));
        registry.Register(new("lmsupply", ProviderKind.Local, 50, _ => backup));
        var seen = new List<ProwTransition>();

        await Gateway(registry, new IronProwOptions { OnTransition = seen.Add }, maxRetries: 2)
            .GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        failing.ModelIds.Should().HaveCount(3, "positive control: a transient 500 is retried");
        seen.Should().OnlyContain(t => t.Reason == ProwFailureReason.Unknown);
    }
}
