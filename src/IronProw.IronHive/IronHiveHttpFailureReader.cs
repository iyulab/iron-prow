using IronHive.Abstractions.Exceptions;
using IronProw.Core;

namespace IronProw.IronHive;

/// <summary>
/// Reads the HTTP failure an ironhive provider exception describes. Ironhive normalizes every provider's rate-limit
/// error (HTTP 429 and vendor equivalents) to <see cref="RateLimitException"/>, and the OpenAI-compatible client's
/// other HTTP errors to <see cref="ProviderHttpException"/>; both carry the provider's retry hint when it sent one. A
/// refusal the account cannot pay (<see cref="BillingException"/> — HTTP 402, or OpenAI <c>insufficient_quota</c> sent as
/// 429) reads as 402: never retried, and like a credential refusal another provider's account is not affected by it.
/// Without this reader the gateway sees neither a rate limit's status nor any hint, and the same 429 or 503 is
/// classified differently depending on whether a provider was registered through ironhive or a raw SDK client.
/// Registered by every <c>AddIronHive*</c> method.
/// </summary>
public sealed class IronHiveHttpFailureReader : IHttpFailureReader
{
    /// <inheritdoc />
    public HttpFailure? Read(Exception exception) => exception switch
    {
        BillingException => new HttpFailure(402, null),
        RateLimitException rateLimit => new HttpFailure(429, rateLimit.RetryAfter),
        ProviderHttpException { StatusCode: { } status } http => new HttpFailure((int)status, http.RetryAfter),
        _ => null
    };
}
