using System.ClientModel;
using System.Text.RegularExpressions;

namespace IronProw.Core;

/// <summary>
/// Recognizes a provider failure whose text says the server could not render the request into the model's prompt -
/// a chat-template error. Self-hosted OpenAI-compatible servers report these as 500 (llama.cpp, GPUStack) or 400
/// (vLLM, Ollama), with the template engine's message in the body; the same request meets the same refusal every time.
/// </summary>
internal static partial class RequestRenderRefusal
{
    // Each pattern is the shape of a template engine's own error text, not a word that may appear in any failure:
    //  - minja (llama.cpp's Jinja engine, also behind GPUStack): «While executing CallExpression at line 46, column 21»
    //  - a template calling its raise helper («raise_exception('Conversation roles must alternate ...')»)
    //  - Python jinja2 (vLLM): «jinja2.exceptions.TemplateError: ...» / «TemplateSyntaxError»
    //  - Mistral / Gemma-style templates: «Conversation roles must alternate user/assistant/...»
    //  - servers that name the step: «Failed to apply chat template», «Error in applying chat template»,
    //    «error rendering chat template»
    //  - Go text/template (Ollama): «template: :12:5: executing "" at <...>: error calling ...»
    [GeneratedRegex(
        @"While executing \w+ at line \d+"
        + @"|\braise_exception\b"
        + @"|\bTemplate(?:Syntax)?Error\b"
        + @"|roles must alternate"
        + @"|\b(?:apply|applying|render|rendering)\b[^.\n]{0,24}\bchat template\b"
        + @"|\btemplate: [^:\n]*:\d+:\d+: executing\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Signature();

    /// <summary>True when <paramref name="exception"/> (or an inner one, or a buffered response body) carries a template-render signature.</summary>
    public static bool Matches(Exception exception)
    {
        var depth = 0;
        for (var ex = exception; ex is not null && depth < 4; ex = ex.InnerException, depth++)
        {
            if (Signature().IsMatch(ex.Message))
                return true;
            if (ex is ClientResultException cre && BodyOf(cre) is { } body && Signature().IsMatch(body))
                return true;
        }
        return false;
    }

    private static string? BodyOf(ClientResultException exception)
    {
        try
        {
            return exception.GetRawResponse()?.Content?.ToString();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // An unbuffered or disposed response body cannot be read after the fact; the message is what there is.
            // (The classifier runs inside exception filters, so reading the body must never throw.)
            return null;
        }
    }
}
