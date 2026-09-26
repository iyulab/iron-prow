using System.Runtime.CompilerServices;
using AwesomeAssertions;
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

        await sut.GetResponseAsync([new(ChatRole.User, "hi")], new ChatOptions { MaxOutputTokens = 256 }, TestContext.Current.CancellationToken);

        gen.LastOptions!.MaxNewTokens.Should().Be(256);
        // Resolved cap (MaxNewTokens ?? MaxTokens) must reflect the explicit value.
        gen.LastOptions.ResolveMaxOutputTokens().Should().Be(256);
    }

    [Fact]
    public async Task Roles_map_to_lmsupply_roles()
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);

        await sut.GetResponseAsync([
                new(ChatRole.System, "sys"),
                new(ChatRole.User, "u"),
                new(ChatRole.Assistant, "a")
            ], new ChatOptions { Instructions = "instr" }, TestContext.Current.CancellationToken);

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

        var response = await sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

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
        await foreach (var u in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
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

    // The backend's usage rides on the final stream chunk; the streaming surface must hand it on as
    // UsageContent so ToChatResponse() reports the same numbers the non-streaming path does.
    [Fact]
    public async Task Streaming_final_chunk_usage_reaches_the_response_as_usage_content()
    {
        var gen = new FakeTextGenerator
        {
            StreamChunks =
            [
                new LmChatStreamChunk { Text = "ok" },
                new LmChatStreamChunk
                {
                    FinishReason = "stop",
                    Usage = new global::LMSupply.Generator.Models.ChatTokenUsage { PromptTokens = 12, CompletionTokens = 480, TotalTokens = 492 }
                }
            ]
        };
        var sut = new GeneratorChatClient(gen);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(u);
        }

        var response = updates.ToChatResponse();
        response.Text.Should().Be("ok");
        response.FinishReason.Should().Be(ChatFinishReason.Stop);
        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokenCount.Should().Be(12);
        response.Usage.OutputTokenCount.Should().Be(480);
        response.Usage.TotalTokenCount.Should().Be(492);
    }

    [Fact]
    public async Task Streaming_without_backend_usage_reports_no_usage()
    {
        var gen = new FakeTextGenerator
        {
            StreamChunks = [new LmChatStreamChunk { Text = "ok" }, new LmChatStreamChunk { FinishReason = "stop" }]
        };
        var sut = new GeneratorChatClient(gen);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(u);
        }

        updates.ToChatResponse().Usage.Should().BeNull();
        updates.Last().Contents.Should().BeEmpty();
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

    // A structured tool result (the shape the IronHive bridge keeps for image results since 0.26.0) used to
    // reach the local model as the CLR type name "Microsoft.Extensions.AI.AIContent[]" — ToString() of a list.
    [Fact]
    public async Task Structured_tool_result_is_flattened_to_text_not_the_clr_type_name()
    {
        var gen = new FakeTextGenerator { CompletionContent = "ok" };
        var sut = new GeneratorChatClient(gen);
        var result = new List<AIContent>
        {
            new TextContent("front page"),
            new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
        };

        await sut.GetResponseAsync([
                new(ChatRole.Assistant, [new FunctionCallContent("call-1", "read_image")]),
                new(ChatRole.Tool, [new FunctionResultContent("call-1", result)]),
            ], cancellationToken: TestContext.Current.CancellationToken);

        var toolMessage = gen.LastMessages.Should().ContainSingle(m => m.Role == LmChatRole.Tool).Subject;
        toolMessage.Content.Should().NotContain("AIContent");
        toolMessage.Content.Should().StartWith("front page");
        toolMessage.Content.Should().Contain("image/png").And.Contain("omitted");
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(42, "42")]
    public void FlattenToolResult_keeps_strings_and_renders_scalars(object result, string expected)
        => GeneratorChatClient.FlattenToolResult(result).Should().Be(expected);

    [Fact]
    public void FlattenToolResult_renders_a_poco_as_json_not_its_type_name()
        => GeneratorChatClient.FlattenToolResult(new { temperature = 22 }).Should().Be("{\"temperature\":22}");

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
        public Task<global::LMSupply.Generator.Models.GenerationResult> GenerateCompleteResultAsync(string prompt, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<global::LMSupply.Generator.Models.GenerationResult> GenerateChatCompleteResultAsync(IEnumerable<LmChatMessage> messages, LmGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WarmupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
