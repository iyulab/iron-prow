using IronHive.Abstractions.Exceptions;
using IronProw.Core;

namespace IronProw.IronHive;

/// <summary>
/// Reads the HTTP failure an ironhive provider exception describes. Ironhive normalizes every provider's rate-limit
/// error (HTTP 429 and vendor equivalents) to <see cref="RateLimitException"/>, which carries the provider's retry
/// hint when it sent one — without this reader the gateway sees neither the status nor the hint, and the same 429
/// is classified differently depending on whether a provider was registered through ironhive or a raw SDK client.
/// Registered by every <c>AddIronHive*</c> method.
/// </summary>
public sealed class IronHiveHttpFailureReader : IHttpFailureReader
{
    /// <inheritdoc />
    public HttpFailure? Read(Exception exception) => exception switch
    {
        RateLimitException rateLimit => new HttpFailure(429, rateLimit.RetryAfter),
        _ => null
    };
}
