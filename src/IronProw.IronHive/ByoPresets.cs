using System.ClientModel;
using IronHive.Abstractions.Models;
using IronHive.Providers.Anthropic;
using IronHive.Providers.GoogleAI;
using IronHive.Providers.OpenAI;
using IronHive.Providers.OpenAI.Compatible;
using IronHive.Providers.OpenAI.Compatible.GpuStack;
using IronProw.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IronProw.IronHive;

/// <summary>The wire protocol a <see cref="ByoPreset"/> speaks — which ironhive provider serves it.</summary>
public enum ByoWire
{
    /// <summary>OpenAI's own API (Chat Completions), through <see cref="IronHiveProviderExtensions.AddIronHiveOpenAI"/>.</summary>
    OpenAI,

    /// <summary>Anthropic's Messages API, through <see cref="IronHiveProviderExtensions.AddIronHiveAnthropic"/>.</summary>
    Anthropic,

    /// <summary>Google's Gemini API, through <see cref="IronHiveProviderExtensions.AddIronHiveGoogleAI"/>.</summary>
    GoogleAI,

    /// <summary>The OpenAI-compatible <c>/v1</c> surface, through <see cref="IronHiveProviderExtensions.AddIronHiveOpenAICompatible"/>.</summary>
    OpenAICompatible,

    /// <summary>GPUStack's <c>/v1-openai</c> surface, through <see cref="IronHiveProviderExtensions.AddIronHiveGpuStack"/>.</summary>
    GpuStack,
}

/// <summary>
/// A bring-your-own endpoint kind an application offers its users: where it is by default, what the user must supply,
/// and which provider serves it. Display names and wording are the application's.
/// </summary>
/// <param name="Id">Stable preset id (<c>openai</c>, <c>anthropic</c>, <c>gemini</c>, <c>grok</c>, <c>ollama</c>, <c>gpustack</c>, <c>custom</c>).</param>
/// <param name="Kind">Where the gateway places it: <see cref="ProviderKind.Frontier"/> or <see cref="ProviderKind.Lan"/>.</param>
/// <param name="Wire">The provider that serves it.</param>
/// <param name="DefaultBaseUrl">The base URL used when the user leaves it empty, or null when the user must supply one.</param>
/// <param name="ApiKeyRequired">Whether the endpoint refuses requests without a key (local servers accept none).</param>
public sealed record ByoPreset(string Id, ProviderKind Kind, ByoWire Wire, string? DefaultBaseUrl, bool ApiKeyRequired)
{
    /// <summary>True when the user must supply a base URL (there is no default).</summary>
    public bool BaseUrlRequired => DefaultBaseUrl is null;
}

/// <summary>What a user entered for one bring-your-own endpoint.</summary>
/// <param name="Preset">A <see cref="ByoPreset.Id"/>.</param>
/// <param name="BaseUrl">The base URL, or null/empty for the preset's default.</param>
/// <param name="ApiKey">The API key, or null/empty for none.</param>
/// <param name="Headers">
/// Extra request headers (a gateway's own token, a routing header), sent by the gateway registration and by
/// <see cref="ByoPresets.ProbeAsync"/> alike, through the provider's <c>Headers</c> setting. The credential is not a
/// header: a header the provider reserves for the key is refused (<see cref="ByoPresets.Validate"/> says which).
/// </param>
public sealed record ByoEndpoint(
    string Preset,
    string? BaseUrl = null,
    string? ApiKey = null,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>Why <see cref="ByoPresets.ProbeAsync"/> failed, for an application that words the failure itself.</summary>
public enum ByoProbeFailure
{
    /// <summary>The endpoint refused the credential: HTTP 401 or 403.</summary>
    Unauthorized = 1,

    /// <summary>The endpoint answered with another HTTP error (<see cref="ByoProbeResult.StatusCode"/> says which).</summary>
    HttpStatus = 2,

    /// <summary>No answer in time: the probe's timeout, or no connection within the provider's connect timeout.</summary>
    Timeout = 3,

    /// <summary>The host is up but nothing listens on the port: the connection was refused.</summary>
    Refused = 4,

    /// <summary>The host name does not resolve.</summary>
    HostNotFound = 5,

    /// <summary>The network or the host cannot be reached at all.</summary>
    Unreachable = 6,

    /// <summary>Any other transport failure: TLS, a reset connection, a malformed response.</summary>
    Transport = 7,

    /// <summary>What the user entered failed <see cref="ByoPresets.Validate"/>; nothing was contacted.</summary>
    Invalid = 8,
}

/// <summary>The outcome of <see cref="ByoPresets.ProbeAsync"/>.</summary>
/// <param name="Ok">The endpoint answered an authenticated model-list request.</param>
/// <param name="ModelIds">The ids of the models it listed, in the server's order (empty when not <see cref="Ok"/>).</param>
/// <param name="StatusCode">The HTTP status of a refusal, when the provider reported one (401 = bad key, 404 = wrong path).</param>
/// <param name="Error">
/// Why it failed, for a person, or null when <see cref="Ok"/>. It is the operating system's or the provider's text: it may be
/// localized and may repeat the address or the provider's details. Classify with <see cref="Failure"/>, not this.
/// </param>
public sealed record ByoProbeResult(bool Ok, IReadOnlyList<string> ModelIds, int? StatusCode, string? Error)
{
    /// <summary>How many models it listed (0 when not <see cref="Ok"/>).</summary>
    public int ModelCount => ModelIds.Count;

    /// <summary>Why it failed, as a kind an application can word in its own language; null when <see cref="Ok"/>.</summary>
    public ByoProbeFailure? Failure { get; init; }

    /// <summary>
    /// True when the endpoint answered and listed <paramref name="modelId"/> (ordinal comparison) - the check an
    /// application makes before sending a request to a model the server does not serve.
    /// </summary>
    public bool Lists(string modelId) => Ok && ModelIds.Contains(modelId, StringComparer.Ordinal);

    internal static ByoProbeResult Failed(ByoProbeFailure failure, int? statusCode, string error) =>
        new(false, [], statusCode, error) { Failure = failure };
}

/// <summary>
/// The catalogue of bring-your-own endpoint presets, with the three things an application needs for each: the rules
/// for what the user entered (<see cref="Validate"/>), the gateway registration (<see cref="AddIronHiveByo"/>), and a
/// connection check that really authenticates (<see cref="ProbeAsync"/> — one model-list call through the provider's
/// own model finder, frontier providers included).
/// </summary>
public static class ByoPresets
{
    /// <summary>Ollama's default address — the OpenAI-compatible provider's default.</summary>
    internal const string OllamaBaseUrl = "http://localhost:11434";

    /// <summary>GPUStack's default address — the GPUStack provider's default.</summary>
    internal const string GpuStackBaseUrl = "http://localhost:8080";

    /// <summary>OpenAI. Base URL optional (the SDK default), key required.</summary>
    public static ByoPreset OpenAI { get; } = new("openai", ProviderKind.Frontier, ByoWire.OpenAI, "https://api.openai.com/v1", ApiKeyRequired: true);

    /// <summary>Anthropic. Base URL optional (the SDK default), key required.</summary>
    public static ByoPreset Anthropic { get; } = new("anthropic", ProviderKind.Frontier, ByoWire.Anthropic, "https://api.anthropic.com", ApiKeyRequired: true);

    /// <summary>Google Gemini. Base URL optional (the SDK default), key required.</summary>
    public static ByoPreset Gemini { get; } = new("gemini", ProviderKind.Frontier, ByoWire.GoogleAI, "https://generativelanguage.googleapis.com", ApiKeyRequired: true);

    /// <summary>xAI Grok, over its OpenAI-compatible API. Key required.</summary>
    public static ByoPreset Grok { get; } = new("grok", ProviderKind.Frontier, ByoWire.OpenAICompatible, "https://api.x.ai/v1", ApiKeyRequired: true);

    /// <summary>A local Ollama server. Key optional.</summary>
    public static ByoPreset Ollama { get; } = new("ollama", ProviderKind.Lan, ByoWire.OpenAICompatible, OllamaBaseUrl, ApiKeyRequired: false);

    /// <summary>A GPUStack server. Key optional.</summary>
    public static ByoPreset GpuStack { get; } = new("gpustack", ProviderKind.Lan, ByoWire.GpuStack, GpuStackBaseUrl, ApiKeyRequired: false);

    /// <summary>Any other OpenAI-compatible server (LM Studio, vLLM, llama.cpp server, a proxy). Base URL required, key optional.</summary>
    public static ByoPreset Custom { get; } = new("custom", ProviderKind.Lan, ByoWire.OpenAICompatible, DefaultBaseUrl: null, ApiKeyRequired: false);

    /// <summary>Every preset, in the order an application would usually list them.</summary>
    public static IReadOnlyList<ByoPreset> All { get; } = [OpenAI, Anthropic, Gemini, Grok, Ollama, GpuStack, Custom];

    /// <summary>The preset with <paramref name="id"/> (case-insensitive), or null when there is none.</summary>
    public static ByoPreset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Checks what the user entered, without contacting anything: a known preset, a base URL that is absolute
    /// <c>http</c>/<c>https</c> when given (and given when the preset has no default), a key when the preset needs one,
    /// and headers the provider accepts (no empty name, none of the names it reserves for the key).
    /// </summary>
    /// <returns>Why the endpoint cannot be used, or null when it can.</returns>
    public static string? Validate(ByoEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (ValidateEntry(endpoint) is { } invalid)
        {
            return invalid;
        }

        if (endpoint.Headers is not { Count: > 0 })
        {
            return null;
        }

        // The header rules are the provider's (IronHive refuses the names that carry its credential, which differ per
        // provider); building the client applies them without sending anything.
        try
        {
            using var finder = CreateModelFinder(endpoint, Find(endpoint.Preset)!);
            return null;
        }
        catch (ArgumentException refused)
        {
            return refused.Message;
        }
    }

    private static string? ValidateEntry(ByoEndpoint endpoint)
    {

        if (Find(endpoint.Preset) is not { } preset)
        {
            return $"Unknown preset '{endpoint.Preset}'. Known presets: {string.Join(", ", All.Select(p => p.Id))}.";
        }

        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            if (preset.BaseUrlRequired)
            {
                return $"The '{preset.Id}' preset needs a base URL.";
            }
        }
        else if (!Uri.TryCreate(endpoint.BaseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"The base URL '{endpoint.BaseUrl}' is not an absolute http or https URL.";
        }

        if (preset.ApiKeyRequired && string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            return $"The '{preset.Id}' preset needs an API key.";
        }

        return null;
    }

    /// <summary>
    /// Registers <paramref name="endpoint"/> as a gateway candidate through the provider its preset names, with the
    /// preset's <see cref="ByoPreset.Kind"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The endpoint fails <see cref="Validate"/>.</exception>
    public static IronProwBuilder AddIronHiveByo(this IronProwBuilder builder, string id, int priority, string modelId, ByoEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var preset = Require(endpoint);
        var entered = Entered.Of(endpoint);

        return preset.Wire switch
        {
            ByoWire.OpenAI => builder.AddIronHiveOpenAI(id, priority, modelId, c => ConfigureOpenAI(c, entered)),
            ByoWire.Anthropic => builder.AddIronHiveAnthropic(id, priority, modelId, c => ConfigureAnthropic(c, entered)),
            ByoWire.GoogleAI => builder.AddIronHiveGoogleAI(id, priority, modelId, c => ConfigureGoogleAI(c, entered)),
            ByoWire.GpuStack => builder.AddIronHiveGpuStack(id, priority, modelId, c => ConfigureGpuStack(c, entered)),
            _ => AddCompatible(builder, preset, id, priority, modelId, entered),
        };
    }

    /// <summary>
    /// Checks the endpoint for real: one model-list request through the provider's own model finder, authenticated
    /// with the key and carrying the endpoint's headers. A wrong key, a wrong address or a server that is down all come
    /// back as not <see cref="ByoProbeResult.Ok"/>, with the HTTP status when the provider reported one; a reachable
    /// endpoint comes back with the ids it lists (<see cref="ByoProbeResult.Lists"/>). An endpoint that fails
    /// <see cref="Validate"/> is not contacted.
    /// </summary>
    /// <remarks>
    /// One request, not the SDK's retry loop: a connection check answers what the endpoint does now, and retrying an
    /// unreachable host only multiplies the wait (four attempts and their backoff, then a message about the retries
    /// instead of the cause). <see cref="ByoProbeResult.Error"/> names the cause - the provider's refusal with its
    /// message, the connection failure, or the timeout.
    /// </remarks>
    /// <param name="endpoint">What the user entered.</param>
    /// <param name="timeout">How long to wait for the answer. Default: 10 seconds.</param>
    /// <param name="cancellationToken">Cancels the check; cancellation is thrown, not reported as a failure.</param>
    public static async Task<ByoProbeResult> ProbeAsync(ByoEndpoint endpoint, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Validate(endpoint) is { } invalid)
        {
            return ByoProbeResult.Failed(ByoProbeFailure.Invalid, null, invalid);
        }

        var budget = timeout ?? TimeSpan.FromSeconds(10);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(budget);
        try
        {
            using var finder = CreateModelFinder(endpoint, Find(endpoint.Preset)!, probe: true);
            var models = await finder.ListModelsAsync(timeoutSource.Token).ConfigureAwait(false);
            return new ByoProbeResult(true, [.. models.Select(m => m.ModelId)], null, null);
        }
        catch (Exception) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ByoProbeResult.Failed(ByoProbeFailure.Timeout, null, $"No answer within {budget.TotalSeconds:0.#} s.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var status = StatusOf(ex);
            return ByoProbeResult.Failed(FailureOf(ex, status), status, ErrorOf(ex));
        }
    }

    // The kind is read from the exception chain, where every SDK keeps the transport's own exception: the status of a
    // refusal, the socket error of a failed connect, or the handler's connect timeout.
    internal static ByoProbeFailure FailureOf(Exception exception, int? status)
    {
        if (status is { } code)
        {
            return code is 401 or 403 ? ByoProbeFailure.Unauthorized : ByoProbeFailure.HttpStatus;
        }

        var canceled = false;
        for (var cause = exception; cause is not null; cause = cause.InnerException)
        {
            canceled |= cause is OperationCanceledException;
            switch (cause)
            {
                case System.Net.Sockets.SocketException socket:
                    return socket.SocketErrorCode switch
                    {
                        System.Net.Sockets.SocketError.ConnectionRefused => ByoProbeFailure.Refused,
                        System.Net.Sockets.SocketError.HostNotFound or System.Net.Sockets.SocketError.NoData
                            or System.Net.Sockets.SocketError.TryAgain => ByoProbeFailure.HostNotFound,
                        System.Net.Sockets.SocketError.HostUnreachable or System.Net.Sockets.SocketError.NetworkUnreachable
                            or System.Net.Sockets.SocketError.HostDown or System.Net.Sockets.SocketError.NetworkDown
                            => ByoProbeFailure.Unreachable,
                        System.Net.Sockets.SocketError.TimedOut => ByoProbeFailure.Timeout,
                        _ => ByoProbeFailure.Transport,
                    };
                case TimeoutException:
                    // SocketsHttpHandler's ConnectTimeout: "A connection could not be established within the configured ConnectTimeout."
                    return ByoProbeFailure.Timeout;
                case HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }:
                    return ByoProbeFailure.HostNotFound;
            }
        }

        // A cancellation that is neither the caller's nor the probe's budget is a provider's own request timeout.
        return canceled ? ByoProbeFailure.Timeout : ByoProbeFailure.Transport;
    }

    // A refusal: System.ClientModel's message is a bare "Service request failed." for any service but OpenAI's own, and
    // the response body is where an OpenAI-compatible server (xAI, a proxy) says what was wrong. Anything else (no
    // connection, a connect timeout): the innermost exception names the cause; the outer ones only wrap it.
    private static string ErrorOf(Exception exception)
    {
        if (exception is ClientResultException result && result.GetRawResponse()?.Content?.ToString() is { Length: > 0 } body)
        {
            var headline = exception.Message.Split('\n', 2)[0].Trim();
            return $"{headline} {System.Text.RegularExpressions.Regex.Replace(body.Trim(), @"\s+", " ")}";
        }

        if (StatusOf(exception) is not null)
        {
            return exception.Message;
        }

        var cause = exception;
        while (cause.InnerException is { } inner)
        {
            cause = inner;
        }

        return cause.Message;
    }

    internal static IModelFinder CreateModelFinder(ByoEndpoint endpoint) =>
        CreateModelFinder(endpoint, Require(endpoint));

    // probe: one request - the SDK's retries off (see ProbeAsync).
    private static IModelFinder CreateModelFinder(ByoEndpoint endpoint, ByoPreset preset, bool probe = false)
    {
        var entered = Entered.Of(endpoint);
        int? maxRetries = probe ? 0 : null;

        switch (preset.Wire)
        {
            case ByoWire.OpenAI:
                var openAI = new OpenAIConfig { MaxRetries = maxRetries };
                ConfigureOpenAI(openAI, entered);
                return new OpenAIModelFinder(openAI);
            case ByoWire.Anthropic:
                var anthropic = new AnthropicConfig { MaxRetries = maxRetries };
                ConfigureAnthropic(anthropic, entered);
                return new AnthropicModelFinder(anthropic);
            case ByoWire.GoogleAI:
                var google = new GoogleAIConfig();
                ConfigureGoogleAI(google, entered, probe ? new Google.GenAI.Types.HttpRetryOptions { Attempts = 1 } : null);
                return new GoogleAIModelFinder(google);
            case ByoWire.GpuStack:
                var gpuStack = new GpuStackConfig { MaxRetries = maxRetries };
                ConfigureGpuStack(gpuStack, entered);
                return new OpenAIModelFinder(gpuStack.ToOpenAICompatible().ToOpenAI());
            default:
                var compatible = CompatibleConfig(preset, entered);
                compatible.MaxRetries = maxRetries;
                return new OpenAIModelFinder(compatible.ToOpenAI());
        }
    }

    private static ByoPreset Require(ByoEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Validate(endpoint) is { } invalid)
        {
            throw new ArgumentException(invalid, nameof(endpoint));
        }

        return Find(endpoint.Preset)!;
    }

    /// <summary>What the user entered, normalized: blank becomes null (the provider's own default, not a restatement of it).</summary>
    private sealed record Entered(string? BaseUrl, string? ApiKey, Dictionary<string, string>? Headers)
    {
        public static Entered Of(ByoEndpoint endpoint) => new(
            string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? null : endpoint.BaseUrl.Trim(),
            string.IsNullOrWhiteSpace(endpoint.ApiKey) ? null : endpoint.ApiKey.Trim(),
            endpoint.Headers is { Count: > 0 } headers ? new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase) : null);
    }

    private static void ConfigureOpenAI(OpenAIConfig config, Entered entered)
    {
        if (entered.BaseUrl is not null) config.BaseUrl = entered.BaseUrl;
        config.ApiKey = entered.ApiKey ?? string.Empty;
        config.Headers = entered.Headers;
    }

    private static void ConfigureAnthropic(AnthropicConfig config, Entered entered)
    {
        if (entered.BaseUrl is not null) config.BaseUrl = entered.BaseUrl;
        config.ApiKey = entered.ApiKey;
        config.Headers = entered.Headers;
    }

    private static void ConfigureGoogleAI(GoogleAIConfig config, Entered entered, Google.GenAI.Types.HttpRetryOptions? retry = null)
    {
        if (entered.BaseUrl is not null || retry is not null)
        {
            config.HttpOptions = new Google.GenAI.Types.HttpOptions { BaseUrl = entered.BaseUrl, RetryOptions = retry };
        }

        config.ApiKey = entered.ApiKey;
        config.Headers = entered.Headers;
    }

    private static void ConfigureGpuStack(GpuStackConfig config, Entered entered)
    {
        if (entered.BaseUrl is not null) config.BaseUrl = entered.BaseUrl;
        config.ApiKey = entered.ApiKey;
        config.Headers = entered.Headers;
    }

    private static OpenAICompatibleConfig CompatibleConfig(ByoPreset preset, Entered entered) => new()
    {
        // Grok's default carries its own /v1; the compatible provider appends /v1 idempotently, so either form works.
        BaseUrl = entered.BaseUrl ?? preset.DefaultBaseUrl,
        ApiKey = entered.ApiKey,
        Headers = entered.Headers,
    };

    private static IronProwBuilder AddCompatible(
        IronProwBuilder builder, ByoPreset preset, string id, int priority, string modelId, Entered entered)
    {
        if (preset.Kind == ProviderKind.Lan)
        {
            return builder.AddIronHiveOpenAICompatible(id, priority, modelId, c =>
            {
                c.BaseUrl = entered.BaseUrl ?? preset.DefaultBaseUrl;
                c.ApiKey = entered.ApiKey;
                c.Headers = entered.Headers;
            });
        }

        // A frontier service over the OpenAI-compatible surface (Grok): same client, placed as a frontier candidate.
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpFailureReader, IronHiveHttpFailureReader>());
        return builder.AddProvider(id, preset.Kind, priority, _ =>
        {
            var generator = new OpenAICompatibleMessageGenerator(CompatibleConfig(preset, entered));
            return new global::IronHive.Extensions.AI.ChatClientAdapter(generator, modelId, preset.Id);
        });
    }

    // Each SDK reports a refused request its own way; a status lets the app tell a bad key (401/403) from a wrong address (404).
    private static int? StatusOf(Exception exception) => exception switch
    {
        ClientResultException { Status: > 0 } result => result.Status,
        global::Anthropic.Exceptions.AnthropicApiException anthropic => (int)anthropic.StatusCode,
        Google.GenAI.ClientError { StatusCode: > 0 } google => google.StatusCode,
        Google.GenAI.ServerError { StatusCode: > 0 } google => google.StatusCode,
        HttpRequestException { StatusCode: { } status } => (int)status,
        _ when exception.InnerException is { } inner => StatusOf(inner),
        _ => null,
    };
}
