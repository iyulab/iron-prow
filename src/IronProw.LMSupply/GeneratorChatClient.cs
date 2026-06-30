using System.Runtime.CompilerServices;
using System.Text.Json;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using LmChatMessage = LMSupply.Generator.Models.ChatMessage;
using LmChatRole = LMSupply.Generator.Models.ChatRole;
using LmChatToolCall = LMSupply.Generator.Models.ChatToolCall;
using LmChatToolDefinition = LMSupply.Generator.Models.ChatToolDefinition;
using LmGenerationOptions = LMSupply.Generator.Models.GenerationOptions;

namespace IronProw.LMSupply;

/// <summary>
/// Shared bridge that adapts an lm-supply <see cref="ITextGenerator"/> (or <see cref="IGeneratorModel"/>)
/// to <see cref="Microsoft.Extensions.AI.IChatClient"/>. Maps message roles, sampler options, tool
/// definitions/calls, and finish reasons between the two surfaces, and flattens lm-supply's structured
/// stream chunks into <see cref="ChatResponseUpdate"/>.
/// </summary>
/// <remarks>
/// <para>
/// lm-supply does not natively expose <c>IChatClient</c>; before this bridge existed each local-inference
/// consumer (ironhive-host, textree, iron-prow) re-implemented the same mapping. This is the canonical
/// shared bridge for the iron-prow safe-inference gateway (rule-of-three).
/// </para>
/// <para>
/// <b>Token mapping:</b> <see cref="ChatOptions.MaxOutputTokens"/> maps to
/// <c>GenerationOptions.MaxNewTokens</c> — the self-documenting target whose name matches the M.E.AI
/// output-cap semantics. lm-supply resolves the effective cap via <c>ResolveMaxOutputTokens()</c>
/// (<c>MaxNewTokens ?? MaxTokens</c>) and both ONNX and llama-server backends apply it as a new-token
/// cap, so the value is honored identically whether mapped to <c>MaxNewTokens</c> or <c>MaxTokens</c>.
/// </para>
/// <para>
/// The generator lifecycle is owned by the caller (e.g. textree's pool / loader); <see cref="Dispose"/>
/// is intentionally a no-op so wrapping this client never disposes a generator the gateway does not own.
/// </para>
/// </remarks>
public sealed class GeneratorChatClient : IChatClient
{
    private readonly ITextGenerator _generator;

    /// <summary>Creates a bridge over a pre-loaded lm-supply generator.</summary>
    /// <param name="generator">The lm-supply text generator to wrap. Lifetime owned by the caller.</param>
    public GeneratorChatClient(ITextGenerator generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    /// <inheritdoc />
    public ChatClientMetadata Metadata => new("LMSupply", null, _generator.ModelId);

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var lmMessages = ConvertMessages(messages, options);
        var genOptions = ConvertOptions(options);

        var result = await _generator.GenerateChatWithToolsAsync(lmMessages, genOptions, cancellationToken)
            .ConfigureAwait(false);

        var contents = new List<AIContent>();
        if (result.Content is not null)
        {
            contents.Add(new TextContent(result.Content));
        }

        if (result.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in result.ToolCalls)
            {
                contents.Add(new FunctionCallContent(tc.Id, tc.FunctionName, ParseArguments(tc.Arguments)));
            }
        }

        var responseMessage = new ChatMessage(ChatRole.Assistant, contents);
        return new ChatResponse(responseMessage)
        {
            FinishReason = MapFinishReason(result.FinishReason)
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var lmMessages = ConvertMessages(messages, options);
        var genOptions = ConvertOptions(options);

        Dictionary<int, (string Id, string Name, string Args)>? toolCallAccumulator = null;

        await foreach (var chunk in _generator.GenerateChatStreamAsync(lmMessages, genOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            if (chunk.Text is not null)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(chunk.Text)]
                };
            }

            if (chunk.ToolCalls is { Count: > 0 })
            {
                toolCallAccumulator ??= [];
                foreach (var delta in chunk.ToolCalls)
                {
                    if (!toolCallAccumulator.TryGetValue(delta.Index, out var existing))
                    {
                        existing = ("", "", "");
                    }

                    toolCallAccumulator[delta.Index] = (
                        delta.Id ?? existing.Id,
                        delta.Name ?? existing.Name,
                        existing.Args + (delta.Arguments ?? ""));
                }
            }

            if (chunk.FinishReason is not null)
            {
                var finishReason = MapFinishReason(chunk.FinishReason);
                if (toolCallAccumulator is { Count: > 0 })
                {
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = BuildToolCallContents(toolCallAccumulator),
                        FinishReason = finishReason
                    };
                    toolCallAccumulator = null;
                }
                else
                {
                    yield return new ChatResponseUpdate { FinishReason = finishReason };
                }
            }
        }

        // Flush accumulated tool calls if the stream ended without a finish-reason chunk.
        if (toolCallAccumulator is { Count: > 0 })
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = BuildToolCallContents(toolCallAccumulator),
                FinishReason = ChatFinishReason.ToolCalls
            };
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType == typeof(IChatClient) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Generator lifecycle is owned by the caller; nothing to dispose here.
    }

    private static IEnumerable<LmChatMessage> ConvertMessages(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        // M.E.AI standard ChatOptions.Instructions: emit as a leading System message so downstream
        // chat formatters apply it like an explicit system prompt.
        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            yield return new LmChatMessage(LmChatRole.System, options.Instructions);
        }

        foreach (var msg in messages)
        {
            var functionCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            var functionResults = msg.Contents.OfType<FunctionResultContent>().ToList();

            if (msg.Role == ChatRole.Assistant && functionCalls.Count > 0)
            {
                var toolCalls = functionCalls.Select(fc => new LmChatToolCall(
                    fc.CallId ?? $"call_{Guid.NewGuid():N}",
                    fc.Name,
                    fc.Arguments is not null ? JsonSerializer.Serialize(fc.Arguments) : "{}")).ToList();

                yield return new LmChatMessage(LmChatRole.Assistant, msg.Text ?? string.Empty)
                {
                    ToolCalls = toolCalls
                };
                continue;
            }

            if (msg.Role == ChatRole.Tool && functionResults.Count > 0)
            {
                foreach (var fr in functionResults)
                {
                    yield return LmChatMessage.ToolResult(fr.CallId ?? "", fr.Result?.ToString() ?? "");
                }
                continue;
            }

            var role = msg.Role.Value switch
            {
                "system" => LmChatRole.System,
                "user" => LmChatRole.User,
                "assistant" => LmChatRole.Assistant,
                "tool" => LmChatRole.Tool,
                _ => LmChatRole.User
            };
            yield return new LmChatMessage(role, msg.Text ?? string.Empty);
        }
    }

    private static LmGenerationOptions? ConvertOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var genOptions = new LmGenerationOptions();

        // Output cap → MaxNewTokens (self-documenting; lm-supply resolves MaxNewTokens ?? MaxTokens).
        if (options.MaxOutputTokens.HasValue)
        {
            genOptions.MaxNewTokens = options.MaxOutputTokens.Value;
        }

        if (options.Temperature.HasValue)
        {
            genOptions.Temperature = options.Temperature.Value;
        }

        if (options.TopP.HasValue)
        {
            genOptions.TopP = options.TopP.Value;
        }

        if (options.TopK.HasValue)
        {
            genOptions.TopK = options.TopK.Value;
        }

        if (options.FrequencyPenalty.HasValue)
        {
            genOptions.FrequencyPenalty = options.FrequencyPenalty.Value;
        }

        if (options.PresencePenalty.HasValue)
        {
            genOptions.PresencePenalty = options.PresencePenalty.Value;
        }

        if (options.Seed.HasValue)
        {
            genOptions.Seed = unchecked((int)options.Seed.Value);
        }

        if (options.StopSequences is { Count: > 0 })
        {
            genOptions.StopSequences = [.. options.StopSequences];
        }

        // lm-supply native sampler params (no standard M.E.AI surface) via the provider-specific
        // AdditionalProperties bag. Keys match lm-supply property names in snake_case. Defaults
        // (RepetitionPenalty=1.1, MinP=0.05) are preserved when unset.
        if (options.AdditionalProperties is { Count: > 0 } extras)
        {
            if (extras.TryGetValue("repetition_penalty", out var rp) && TryToFloat(rp, out var rpVal))
            {
                genOptions.RepetitionPenalty = rpVal;
            }
            if (extras.TryGetValue("min_p", out var mp) && TryToFloat(mp, out var mpVal))
            {
                genOptions.MinP = mpVal;
            }
        }

        if (options.Tools is { Count: > 0 })
        {
            var tools = new List<LmChatToolDefinition>();
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction func)
                {
                    var parameters = func.JsonSchema.ValueKind != JsonValueKind.Undefined
                        ? func.JsonSchema
                        : (JsonElement?)null;
                    tools.Add(new LmChatToolDefinition(func.Name, func.Description, parameters));
                }
            }
            if (tools.Count > 0)
            {
                genOptions.Tools = tools;
            }
        }

        return genOptions;
    }

    private static bool TryToFloat(object? value, out float result)
    {
        switch (value)
        {
            case float f:
                result = f;
                return true;
            case double d:
                result = (float)d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case string s when float.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed):
                result = parsed;
                return true;
            default:
                result = 0f;
                return false;
        }
    }

    private static List<AIContent> BuildToolCallContents(
        Dictionary<int, (string Id, string Name, string Args)> accumulator)
    {
        var contents = new List<AIContent>();
        foreach (var (_, (id, name, args)) in accumulator.OrderBy(kvp => kvp.Key))
        {
            if (string.IsNullOrEmpty(name))
            {
                continue; // skip malformed deltas without a function name
            }

            var callId = string.IsNullOrEmpty(id) ? $"call_{Guid.NewGuid():N}" : id;
            contents.Add(new FunctionCallContent(callId, name, ParseArguments(args)));
        }
        return contents;
    }

    private static Dictionary<string, object?>? ParseArguments(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ChatFinishReason? MapFinishReason(string? reason) => reason switch
    {
        "stop" => ChatFinishReason.Stop,
        "tool_calls" => ChatFinishReason.ToolCalls,
        "length" => ChatFinishReason.Length,
        _ => null
    };
}
