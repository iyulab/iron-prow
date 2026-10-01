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
public sealed record ByoEndpoint(string Preset, string? BaseUrl = null, string? ApiKey = null);

/// <summary>The outcome of <see cref="ByoPresets.ProbeAsync"/>.</summary>
/// <param name="Ok">The endpoint answered an authenticated model-list request.</param>
/// <param name="ModelCount">How many models it listed (0 when not <see cref="Ok"/>).</param>
/// <param name="StatusCode">The HTTP status of a refusal, when the provider reported one (401 = bad key, 404 = wrong path).</param>
/// <param name="Error">Why it failed, or null when <see cref="Ok"/>.</param>
public sealed record ByoProbeResult(bool Ok, int ModelCount, int? StatusCode, string? Error);

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
    /// <c>http</c>/<c>https</c> when given (and given when the preset has no default), and a key when the preset needs one.
    /// </summary>
    /// <returns>Why the endpoint cannot be used, or null when it can.</returns>
    public static string? Validate(ByoEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

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
        var baseUrl = BaseUrlOf(endpoint);
        var apiKey = string.IsNullOrWhiteSpace(endpoint.ApiKey) ? null : endpoint.ApiKey.Trim();

        return preset.Wire switch
        {
            ByoWire.OpenAI => builder.AddIronHiveOpenAI(id, priority, modelId, c => ConfigureOpenAI(c, baseUrl, apiKey)),
            ByoWire.Anthropic => builder.AddIronHiveAnthropic(id, priority, modelId, c => ConfigureAnthropic(c, baseUrl, apiKey)),
            ByoWire.GoogleAI => builder.AddIronHiveGoogleAI(id, priority, modelId, c => ConfigureGoogleAI(c, baseUrl, apiKey)),
            ByoWire.GpuStack => builder.AddIronHiveGpuStack(id, priority, modelId, c => ConfigureGpuStack(c, baseUrl, apiKey)),
            _ => AddCompatible(builder, preset, id, priority, modelId, baseUrl, apiKey),
        };
    }

    /// <summary>
    /// Checks the endpoint for real: one model-list request through the provider's own model finder, authenticated
    /// with the key. A wrong key, a wrong address or a server that is down all come back as not <see cref="ByoProbeResult.Ok"/>,
    /// with the HTTP status when the provider reported one. An endpoint that fails <see cref="Validate"/> is not contacted.
    /// </summary>
    /// <param name="endpoint">What the user entered.</param>
    /// <param name="timeout">How long to wait for the answer. Default: 10 seconds.</param>
    /// <param name="cancellationToken">Cancels the check; cancellation is thrown, not reported as a failure.</param>
    public static async Task<ByoProbeResult> ProbeAsync(ByoEndpoint endpoint, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Validate(endpoint) is { } invalid)
        {
            return new ByoProbeResult(false, 0, null, invalid);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            using var finder = CreateModelFinder(endpoint);
            var models = await finder.ListModelsAsync(timeoutSource.Token).ConfigureAwait(false);
            return new ByoProbeResult(true, models.Count(), null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ByoProbeResult(false, 0, null, $"No answer within {(timeout ?? TimeSpan.FromSeconds(10)).TotalSeconds:0.#} s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ByoProbeResult(false, 0, StatusOf(ex), ErrorOf(ex));
        }
    }

    // System.ClientModel's message is a bare "Service request failed." for any service but OpenAI's own; the response
    // body is where an OpenAI-compatible server (xAI, a proxy) says what was wrong.
    private static string ErrorOf(Exception exception)
    {
        if (exception is ClientResultException result && result.GetRawResponse()?.Content?.ToString() is { Length: > 0 } body)
        {
            var headline = exception.Message.Split('\n', 2)[0].Trim();
            return $"{headline} {System.Text.RegularExpressions.Regex.Replace(body.Trim(), @"\s+", " ")}";
        }

        return exception.Message;
    }

    internal static IModelFinder CreateModelFinder(ByoEndpoint endpoint)
    {
        var preset = Require(endpoint);
        var baseUrl = BaseUrlOf(endpoint);
        var apiKey = string.IsNullOrWhiteSpace(endpoint.ApiKey) ? null : endpoint.ApiKey.Trim();

        switch (preset.Wire)
        {
            case ByoWire.OpenAI:
                var openAI = new OpenAIConfig();
                ConfigureOpenAI(openAI, baseUrl, apiKey);
                return new OpenAIModelFinder(openAI);
            case ByoWire.Anthropic:
                var anthropic = new AnthropicConfig();
                ConfigureAnthropic(anthropic, baseUrl, apiKey);
                return new AnthropicModelFinder(anthropic);
            case ByoWire.GoogleAI:
                var google = new GoogleAIConfig();
                ConfigureGoogleAI(google, baseUrl, apiKey);
                return new GoogleAIModelFinder(google);
            case ByoWire.GpuStack:
                var gpuStack = new GpuStackConfig();
                ConfigureGpuStack(gpuStack, baseUrl, apiKey);
                return new OpenAIModelFinder(gpuStack.ToOpenAICompatible().ToOpenAI());
            default:
                return new OpenAIModelFinder(CompatibleConfig(preset, baseUrl, apiKey).ToOpenAI());
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

    // Null keeps the provider's own default (the SDK's for frontier providers) instead of restating it.
    private static string? BaseUrlOf(ByoEndpoint endpoint) =>
        string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? null : endpoint.BaseUrl.Trim();

    private static void ConfigureOpenAI(OpenAIConfig config, string? baseUrl, string? apiKey)
    {
        if (baseUrl is not null) config.BaseUrl = baseUrl;
        config.ApiKey = apiKey ?? string.Empty;
    }

    private static void ConfigureAnthropic(AnthropicConfig config, string? baseUrl, string? apiKey)
    {
        if (baseUrl is not null) config.BaseUrl = baseUrl;
        config.ApiKey = apiKey;
    }

    private static void ConfigureGoogleAI(GoogleAIConfig config, string? baseUrl, string? apiKey)
    {
        if (baseUrl is not null) config.HttpOptions = new Google.GenAI.Types.HttpOptions { BaseUrl = baseUrl };
        config.ApiKey = apiKey;
    }

    private static void ConfigureGpuStack(GpuStackConfig config, string? baseUrl, string? apiKey)
    {
        if (baseUrl is not null) config.BaseUrl = baseUrl;
        config.ApiKey = apiKey;
    }

    private static OpenAICompatibleConfig CompatibleConfig(ByoPreset preset, string? baseUrl, string? apiKey) => new()
    {
        // Grok's default carries its own /v1; the compatible provider appends /v1 idempotently, so either form works.
        BaseUrl = baseUrl ?? preset.DefaultBaseUrl,
        ApiKey = apiKey,
    };

    private static IronProwBuilder AddCompatible(
        IronProwBuilder builder, ByoPreset preset, string id, int priority, string modelId, string? baseUrl, string? apiKey)
    {
        if (preset.Kind == ProviderKind.Lan)
        {
            return builder.AddIronHiveOpenAICompatible(id, priority, modelId, c =>
            {
                c.BaseUrl = baseUrl ?? preset.DefaultBaseUrl;
                c.ApiKey = apiKey;
            });
        }

        // A frontier service over the OpenAI-compatible surface (Grok): same client, placed as a frontier candidate.
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpFailureReader, IronHiveHttpFailureReader>());
        return builder.AddProvider(id, preset.Kind, priority, _ =>
        {
            var generator = new OpenAICompatibleMessageGenerator(CompatibleConfig(preset, baseUrl, apiKey));
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
