using System.Runtime.CompilerServices;
using AwesomeAssertions;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Xunit;
using LmChatCompletionResult = LMSupply.Generator.Models.ChatCompletionResult;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmChatStreamChunk = LMSupply.Generator.Models.ChatStreamChunk;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;
using ThinkingMode = LMSupply.Generator.Models.ThinkingMode;
using ChatTokenUsage = LMSupply.Generator.Models.ChatTokenUsage;

namespace IronProw.LMSupply.Tests;

/// <summary>
/// The bridge honours the standard <see cref="ChatOptions.Reasoning"/> slot and carries back what the
/// generator reports about the call. Before 0.4.0 it did neither: a thinking model could spend the whole
/// output budget on reasoning and the consumer received an empty string with FinishReason.Length and no
/// way to see why, and ModelId / Usage / ChatClientMetadata were never filled although the generator
/// knew them. These facts pin the mapping without a model: effort → ThinkingMode, reasoning →
/// TextReasoningContent (dropped only on ReasoningOutput.None), usage/model id → ChatResponse, and
/// GetService(ChatClientMetadata) → Metadata.
/// </summary>
public class GeneratorChatClientReasoningTests
{
    [Theory]
    [InlineData(null, ThinkingMode.Auto)]
    [InlineData(ReasoningEffort.None, ThinkingMode.Off)]
    [InlineData(ReasoningEffort.Low, ThinkingMode.On)]
    [InlineData(ReasoningEffort.High, ThinkingMode.On)]
    public async Task Reasoning_effort_maps_to_thinking_mode(ReasoningEffort? effort, ThinkingMode expected)
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);
        var options = effort is null ? new ChatOptions() : new ChatOptions { Reasoning = new ReasoningOptions { Effort = effort } };

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], options, TestContext.Current.CancellationToken);

        gen.LastOptions!.Thinking.Should().Be(expected);
    }

    [Fact]
    public async Task Reasoning_is_carried_as_TextReasoningContent_when_the_answer_is_empty()
    {
        // The exact shape of the empty-answer failure: budget spent on reasoning, finish=length, no text.
        var gen = new FakeTextGenerator
        {
            CompletionContent = "",
            CompletionReasoning = "First I outline the essay...",
            CompletionFinishReason = "length"
        };
        var sut = new GeneratorChatClient(gen);

        var response = await sut.GetResponseAsync([new(ChatRole.User, "essay")], cancellationToken: TestContext.Current.CancellationToken);

        response.FinishReason.Should().Be(ChatFinishReason.Length);
        response.Text.Should().BeEmpty();
        var reasoning = response.Messages.Single().Contents.OfType<TextReasoningContent>().Single();
        reasoning.Text.Should().Be("First I outline the essay...", "the consumer must be able to see where the budget went");
        gen.LastOptions!.ExtractReasoningTokens.Should().BeTrue("no options were passed; the default request still carries reasoning back");
    }

    [Fact]
    public async Task Reasoning_is_dropped_when_output_is_None()
    {
        var gen = new FakeTextGenerator { CompletionContent = "answer", CompletionReasoning = "thinking" };
        var sut = new GeneratorChatClient(gen);

        var response = await sut.GetResponseAsync(
            [new(ChatRole.User, "hi")],
            new ChatOptions { Reasoning = new ReasoningOptions { Output = ReasoningOutput.None } },
            TestContext.Current.CancellationToken);

        response.Messages.Single().Contents.OfType<TextReasoningContent>().Should().BeEmpty();
        response.Text.Should().Be("answer");
        gen.LastOptions!.ExtractReasoningTokens.Should().BeFalse();
    }

    [Fact]
    public async Task Reasoning_extraction_is_requested_unless_output_is_None()
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { MaxOutputTokens = 8 }, TestContext.Current.CancellationToken);

        gen.LastOptions!.ExtractReasoningTokens.Should().BeTrue("lm-supply only surfaces reasoning deltas when extraction is requested");
    }

    [Fact]
    public async Task Usage_and_model_id_reach_the_response()
    {
        var gen = new FakeTextGenerator
        {
            CompletionContent = "ok",
            CompletionUsage = new ChatTokenUsage { PromptTokens = 12, CompletionTokens = 5, TotalTokens = 17 }
        };
        var sut = new GeneratorChatClient(gen);

        var response = await sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        response.ModelId.Should().Be("fake-model");
        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokenCount.Should().Be(12);
        response.Usage.OutputTokenCount.Should().Be(5);
        response.Usage.TotalTokenCount.Should().Be(17);
    }

    [Fact]
    public async Task Usage_is_null_when_the_backend_reports_none()
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);

        var response = await sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        response.Usage.Should().BeNull();
        response.ModelId.Should().Be("fake-model");
    }

    [Fact]
    public void GetService_answers_ChatClientMetadata()
    {
        var sut = new GeneratorChatClient(new FakeTextGenerator());

        var metadata = sut.GetService<ChatClientMetadata>();

        metadata.Should().NotBeNull();
        metadata!.ProviderName.Should().Be("LMSupply");
        metadata.DefaultModelId.Should().Be("fake-model");
    }

    [Fact]
    public async Task Streaming_carries_reasoning_deltas_and_model_id()
    {
        var gen = new FakeTextGenerator
        {
            StreamChunks =
            [
                new LmChatStreamChunk { ReasoningDelta = "think " },
                new LmChatStreamChunk { ReasoningDelta = "more" },
                new LmChatStreamChunk { Text = "answer" },
                new LmChatStreamChunk { FinishReason = "stop" }
            ]
        };
        var sut = new GeneratorChatClient(gen);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(u);
        }

        string.Concat(updates.SelectMany(u => u.Contents).OfType<TextReasoningContent>().Select(r => r.Text)).Should().Be("think more");
        string.Concat(updates.SelectMany(u => u.Contents).OfType<TextContent>().Select(t => t.Text)).Should().Be("answer");
        updates.Should().OnlyContain(u => u.ModelId == "fake-model");
        updates.Last().FinishReason.Should().Be(ChatFinishReason.Stop);
    }

    [Fact]
    public async Task Streaming_drops_reasoning_deltas_when_output_is_None()
    {
        var gen = new FakeTextGenerator
        {
            StreamChunks = [new LmChatStreamChunk { ReasoningDelta = "think" }, new LmChatStreamChunk { Text = "answer", FinishReason = "stop" }]
        };
        var sut = new GeneratorChatClient(gen);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync(
            [new(ChatRole.User, "hi")],
            new ChatOptions { Reasoning = new ReasoningOptions { Output = ReasoningOutput.None } },
            TestContext.Current.CancellationToken))
        {
            updates.Add(u);
        }

        updates.SelectMany(u => u.Contents).OfType<TextReasoningContent>().Should().BeEmpty();
        string.Concat(updates.SelectMany(u => u.Contents).OfType<TextContent>().Select(t => t.Text)).Should().Be("answer");
    }

    private sealed class FakeTextGenerator : ITextGenerator
    {
        public string ModelId => "fake-model";
        public string? CompletionContent { get; set; }
        public string? CompletionReasoning { get; set; }
        public ChatTokenUsage? CompletionUsage { get; set; }
        public string? CompletionFinishReason { get; set; }
        public IReadOnlyList<LmChatStreamChunk> StreamChunks { get; set; } = [];
        public LmGenerationOptions? LastOptions { get; private set; }

        public Task<LmChatCompletionResult> GenerateChatWithToolsAsync(
            IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new LmChatCompletionResult
            {
                Content = CompletionContent,
                Reasoning = CompletionReasoning,
                Usage = CompletionUsage,
                FinishReason = CompletionFinishReason
            });
        }

        public async IAsyncEnumerable<LmChatStreamChunk> GenerateChatStreamAsync(
            IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            foreach (var chunk in StreamChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
            await Task.CompletedTask;
        }

        public IAsyncEnumerable<string> GenerateAsync(string prompt, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<string> GenerateChatAsync(IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> GenerateCompleteAsync(string prompt, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> GenerateChatCompleteAsync(IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<global::LMSupply.Generator.Models.GenerationResult> GenerateCompleteResultAsync(string prompt, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<global::LMSupply.Generator.Models.GenerationResult> GenerateChatCompleteResultAsync(IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WarmupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
