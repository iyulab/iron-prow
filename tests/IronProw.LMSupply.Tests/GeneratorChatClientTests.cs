using System.Runtime.CompilerServices;
using FluentAssertions;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Xunit;
using LmChatCompletionResult = LMSupply.Generator.Models.ChatCompletionResult;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmChatRole = LMSupply.Generator.Models.ChatRole;
using LmChatStreamChunk = LMSupply.Generator.Models.ChatStreamChunk;
using LmChatToolCall = LMSupply.Generator.Models.ChatToolCall;
using LmChatToolCallDelta = LMSupply.Generator.Models.ChatToolCallDelta;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;

namespace IronProw.LMSupply.Tests;

public class GeneratorChatClientTests
{
    [Fact]
    public async Task MaxOutputTokens_maps_to_MaxNewTokens()
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { MaxOutputTokens = 256 });

        gen.LastOptions!.MaxNewTokens.Should().Be(256);
        // Resolved cap (MaxNewTokens ?? MaxTokens) must reflect the explicit value.
        gen.LastOptions.ResolveMaxOutputTokens().Should().Be(256);
    }

    [Fact]
    public async Task Roles_map_to_lmsupply_roles()
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);

        await sut.GetResponseAsync(
            [
                new(ChatRole.System, "sys"),
                new(ChatRole.User, "u"),
                new(ChatRole.Assistant, "a")
            ],
            new ChatOptions { Instructions = "instr" });

        // Instructions is emitted as a leading System message, then the three messages in order.
        gen.LastMessages.Select(m => m.Role).Should().Equal(
            LmChatRole.System, LmChatRole.System, LmChatRole.User, LmChatRole.Assistant);
        gen.LastMessages[0].Content.Should().Be("instr");
    }

    [Fact]
    public async Task Response_maps_content_tool_calls_and_finish_reason()
    {
        var gen = new FakeTextGenerator
        {
            CompletionContent = "answer",
            CompletionToolCalls = [new LmChatToolCall("call_1", "do_it", "{\"x\":1}")],
            CompletionFinishReason = "tool_calls"
        };
        var sut = new GeneratorChatClient(gen);

        var response = await sut.GetResponseAsync([new(ChatRole.User, "hi")]);

        response.FinishReason.Should().Be(ChatFinishReason.ToolCalls);
        var msg = response.Messages.Single();
        msg.Contents.OfType<TextContent>().Single().Text.Should().Be("answer");
        var call = msg.Contents.OfType<FunctionCallContent>().Single();
        call.CallId.Should().Be("call_1");
        call.Name.Should().Be("do_it");
    }

    [Fact]
    public async Task Streaming_flattens_text_and_accumulated_tool_calls()
    {
        var gen = new FakeTextGenerator
        {
            StreamChunks =
            [
                new LmChatStreamChunk { Text = "Hel" },
                new LmChatStreamChunk { Text = "lo" },
                new LmChatStreamChunk
                {
                    ToolCalls = [new LmChatToolCallDelta { Index = 0, Id = "c1", Name = "fn", Arguments = "{\"a\":" }]
                },
                new LmChatStreamChunk
                {
                    ToolCalls = [new LmChatToolCallDelta { Index = 0, Arguments = "1}" }],
                    FinishReason = "tool_calls"
                }
            ]
        };
        var sut = new GeneratorChatClient(gen);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")]))
        {
            updates.Add(u);
        }

        var text = string.Concat(updates.SelectMany(u => u.Contents.OfType<TextContent>()).Select(t => t.Text));
        text.Should().Be("Hello");

        var call = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).Single();
        call.CallId.Should().Be("c1");
        call.Name.Should().Be("fn");
        // Accumulated arguments parse to a single object spanning both deltas.
        call.Arguments!["a"].Should().NotBeNull();
        updates.Last().FinishReason.Should().Be(ChatFinishReason.ToolCalls);
    }

    [Fact]
    public async Task Cancellation_token_is_propagated_to_generator()
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);
        using var cts = new CancellationTokenSource();

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: cts.Token);

        gen.LastToken.Should().Be(cts.Token);
    }

    [Fact]
    public void GetService_returns_self_for_IChatClient()
    {
        var sut = new GeneratorChatClient(new FakeTextGenerator());
        sut.GetService(typeof(IChatClient)).Should().BeSameAs(sut);
        sut.GetService(typeof(string)).Should().BeNull();
    }

    private sealed class FakeTextGenerator : ITextGenerator
    {
        public string ModelId => "fake-model";
        public string? CompletionContent { get; set; }
        public IReadOnlyList<LmChatToolCall>? CompletionToolCalls { get; set; }
        public string? CompletionFinishReason { get; set; }
        public IReadOnlyList<LmChatStreamChunk> StreamChunks { get; set; } = [];

        public List<LmChatMessage> LastMessages { get; private set; } = [];
        public LmGenerationOptions? LastOptions { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<LmChatCompletionResult> GenerateChatWithToolsAsync(
            IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            LastToken = cancellationToken;
            return Task.FromResult(new LmChatCompletionResult
            {
                Content = CompletionContent,
                ToolCalls = CompletionToolCalls,
                FinishReason = CompletionFinishReason
            });
        }

        public async IAsyncEnumerable<LmChatStreamChunk> GenerateChatStreamAsync(
            IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            LastOptions = options;
            LastToken = cancellationToken;
            foreach (var chunk in StreamChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
            await Task.CompletedTask;
        }

        // Unused surface for these tests.
        public IAsyncEnumerable<string> GenerateAsync(string prompt, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<string> GenerateChatAsync(IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> GenerateCompleteAsync(string prompt, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> GenerateChatCompleteAsync(IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WarmupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
