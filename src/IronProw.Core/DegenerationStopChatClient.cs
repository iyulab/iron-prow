using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>How <see cref="DegenerationDetector"/> recognizes a generation stuck repeating itself.</summary>
public sealed class DegenerationOptions
{
    /// <summary>How many trailing characters of the output are inspected. Default 240.</summary>
    public int Window { get; set; } = 240;

    /// <summary>The longest repeating unit, in characters. Default 60.</summary>
    public int MaxPeriod { get; set; } = 60;

    /// <summary>How many back-to-back copies of a unit at the end of the output count as degeneration. Default 4.</summary>
    public int MinRepeats { get; set; } = 4;
}

/// <summary>
/// Recognizes the repetition loop small models fall into ("concisely concisely concisely …"): the end of the output is
/// one short unit repeated back to back. A unit with no letter in it is never degeneration — Markdown structure and
/// punctuation runs (<c>----</c>, <c>| --- |</c>, <c>====</c>, <c>....</c>, <c>!!!!</c>) repeat legitimately.
/// Biased toward letting a good answer through: a miss costs tokens, a false stop costs the answer.
/// </summary>
public static class DegenerationDetector
{
    /// <summary>
    /// Returns the repeating unit when the end of <paramref name="text"/> is <see cref="DegenerationOptions.MinRepeats"/>
    /// or more back-to-back copies of a unit of at most <see cref="DegenerationOptions.MaxPeriod"/> characters that
    /// contains a letter; otherwise null. Only the last <see cref="DegenerationOptions.Window"/> characters are read.
    /// </summary>
    public static string? FindRepeatingUnit(ReadOnlySpan<char> text, DegenerationOptions? options = null)
    {
        options ??= Defaults;
        var repeats = Math.Max(2, options.MinRepeats);
        var tail = text.Length > options.Window ? text[^options.Window..] : text;
        if (tail.Length < repeats * 2)
            return null;

        var maxPeriod = Math.Min(options.MaxPeriod, tail.Length / repeats);
        for (var period = 1; period <= maxPeriod; period++)
        {
            var span = tail[^(period * repeats)..];
            var periodic = true;
            for (var i = period; i < span.Length; i++)
            {
                if (span[i] != span[i - period])
                {
                    periodic = false;
                    break;
                }
            }
            if (!periodic)
                continue;

            var unit = span[^period..];
            foreach (var c in unit)
            {
                if (char.IsLetter(c))
                    return unit.ToString();
            }
        }
        return null;
    }

    private static readonly DegenerationOptions Defaults = new();
}

/// <summary>
/// Stops a generation that has degenerated into repetition (see <see cref="DegenerationDetector"/>) instead of letting it
/// run to the token limit. Streaming: each update's text is checked against the output so far; on detection the inner
/// stream is abandoned (which cancels the generation) and one final update carries
/// <see cref="FinishReason"/> and the repeated unit under <see cref="RepeatedUnitKey"/> — the stop is a signal, never a
/// silent end. Text already yielded stays yielded. Non-streaming: the call is served through the inner client's stream so
/// it can stop early, and the aggregated response carries the same finish reason. Opt-in; wrap any client, the gateway
/// included: <c>gateway.WithDegenerationStop()</c> or <c>builder.Use(inner =&gt; new DegenerationStopChatClient(inner))</c>.
/// </summary>
public sealed class DegenerationStopChatClient(IChatClient inner, DegenerationOptions? options = null) : DelegatingChatClient(inner)
{
    /// <summary>The finish reason of a generation this client stopped.</summary>
    public static readonly ChatFinishReason FinishReason = new("degeneration");

    /// <summary>The <see cref="ChatResponseUpdate.AdditionalProperties"/> key holding the repeated unit.</summary>
    public const string RepeatedUnitKey = "ironprow.degeneration.unit";

    private readonly DegenerationOptions _options = options ?? new DegenerationOptions();

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => await GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The detector reads only the last Window characters; keep a little more so a unit straddling updates is whole.
        var keep = Math.Max(_options.Window, 1) * 2;
        var tail = new System.Text.StringBuilder();
        string? responseId = null;
        string? messageId = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            responseId ??= update.ResponseId;
            messageId ??= update.MessageId;
            yield return update;

            var text = update.Text;
            if (string.IsNullOrEmpty(text))
                continue;
            tail.Append(text);
            if (tail.Length > keep)
                tail.Remove(0, tail.Length - keep);

            if (DegenerationDetector.FindRepeatingUnit(tail.ToString(), _options) is { } unit)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    ResponseId = responseId,
                    MessageId = messageId,
                    FinishReason = FinishReason,
                    AdditionalProperties = new AdditionalPropertiesDictionary { [RepeatedUnitKey] = unit }
                };
                yield break; // leaving the loop disposes the inner enumerator, which cancels the generation
            }
        }
    }
}

/// <summary>Extension methods for <see cref="DegenerationStopChatClient"/>.</summary>
public static class DegenerationStopChatClientExtensions
{
    /// <summary>Wraps <paramref name="client"/> so a generation that degenerates into repetition is stopped and reported.</summary>
    public static IChatClient WithDegenerationStop(this IChatClient client, Action<DegenerationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        var options = new DegenerationOptions();
        configure?.Invoke(options);
        return new DegenerationStopChatClient(client, options);
    }
}
