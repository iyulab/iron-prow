using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;

namespace IronProw.Core;

/// <summary>
/// The HTTP failure an inference exception describes: the response status and, when the provider sent one,
/// how long it asked the caller to wait before trying again.
/// </summary>
/// <param name="StatusCode">The HTTP status code of the failed response.</param>
/// <param name="RetryAfter">The provider's retry hint (<c>Retry-After</c> / <c>retry-after-ms</c>), or null when absent.</param>
public readonly record struct HttpFailure(int StatusCode, TimeSpan? RetryAfter);

/// <summary>
/// Reads the <see cref="HttpFailure"/> an exception describes. Providers surface HTTP errors as different exception
/// types (the SDK's own, a provider library's normalized one), so the gateway asks every registered reader and uses
/// the first answer. <see cref="DefaultErrorClassifier"/> classifies by it and <see cref="ResilienceChatClient"/>
/// waits the provider's retry hint. Register an extra reader with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IHttpFailureReader, MyReader&gt;())</c>.
/// </summary>
public interface IHttpFailureReader
{
    /// <summary>Returns the HTTP failure <paramref name="exception"/> describes, or null when it describes none.</summary>
    HttpFailure? Read(Exception exception);
}

/// <summary>
/// The built-in reader: <see cref="ClientResultException"/> (the OpenAI SDK and other System.ClientModel clients —
/// status plus the <c>retry-after-ms</c> / <c>Retry-After</c> response headers) and
/// <see cref="HttpRequestException"/> with a <see cref="HttpRequestException.StatusCode"/> (no headers available).
/// </summary>
public sealed class HttpStatusFailureReader : IHttpFailureReader
{
    /// <inheritdoc />
    public HttpFailure? Read(Exception exception) => exception switch
    {
        ClientResultException { Status: > 0 } cre => new HttpFailure(cre.Status, RetryAfterOf(cre.GetRawResponse())),
        HttpRequestException { StatusCode: { } code } => new HttpFailure((int)code, null),
        _ => null
    };

    private static TimeSpan? RetryAfterOf(PipelineResponse? response)
    {
        if (response is null)
            return null;
        if (response.Headers.TryGetValue("retry-after-ms", out var ms)
            && double.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var millis) && millis >= 0)
            return TimeSpan.FromMilliseconds(millis);
        if (response.Headers.TryGetValue("Retry-After", out var value) && value is not null)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                return TimeSpan.FromSeconds(seconds);
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                var wait = at - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
        }
        return null;
    }
}

internal static class HttpFailureReaders
{
    internal static readonly IHttpFailureReader[] BuiltIn = [new HttpStatusFailureReader()];

    internal static HttpFailure? Read(IReadOnlyList<IHttpFailureReader> readers, Exception exception)
    {
        foreach (var reader in readers)
        {
            if (reader.Read(exception) is { } failure)
                return failure;
        }
        return null;
    }
}
