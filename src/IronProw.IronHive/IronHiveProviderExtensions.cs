using IronHive.Core.Microsoft;
using IronHive.Providers.Anthropic;
using IronHive.Providers.GoogleAI;
using IronHive.Providers.OpenAI;
using IronHive.Providers.OpenAI.Compatible;
using IronHive.Providers.OpenAI.Compatible.GpuStack;
using IronProw.Core;

namespace IronProw.IronHive;

/// <summary>
/// Extension methods that register ironhive providers as iron-prow gateway candidates.
/// </summary>
public static class IronHiveProviderExtensions
{
    /// <summary>
    /// Registers an ironhive OpenAI provider as a <see cref="ProviderKind.Frontier"/> candidate.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="modelId">Default model id forwarded to the chat client metadata.</param>
    /// <param name="configure">Action to configure <see cref="OpenAIConfig"/> (API key, base URL, etc.).</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IronProwBuilder AddIronHiveOpenAI(
        this IronProwBuilder builder,
        string id,
        int priority,
        string modelId,
        Action<OpenAIConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddProvider(id, ProviderKind.Frontier, priority, _ =>
        {
            // Preserve the deployed wire protocol: this adapter has always spoken Chat Completions
            // (0.2.1 on ironhive 0.7.9; 0.2.2 pinned OpenAIApiSurface.ChatCompletions on 0.8.2).
            // ironhive >= 0.8.3 reverted the API-surface switch to dedicated generators per package —
            // OpenAIMessageGenerator is now Responses-only, and Chat Completions lives in
            // Compatible.ChatCompletion — so a naive rebuild against OpenAIMessageGenerator would
            // silently flip this gateway's wire protocol. A first-party Responses registration can be
            // added as a separate extension when a consumer demands it.
            var config = new OpenAIConfig();
            configure(config);
            var generator = new global::IronHive.Providers.OpenAI.Compatible.ChatCompletion.ChatCompletionMessageGenerator(config);
            return new ChatClientAdapter(generator, modelId, "openai");
        });
    }

    /// <summary>
    /// Registers an ironhive Anthropic provider as a <see cref="ProviderKind.Frontier"/> candidate.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="modelId">Default model id forwarded to the chat client metadata.</param>
    /// <param name="configure">Action to configure <see cref="AnthropicConfig"/> (API key, etc.).</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IronProwBuilder AddIronHiveAnthropic(
        this IronProwBuilder builder,
        string id,
        int priority,
        string modelId,
        Action<AnthropicConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddProvider(id, ProviderKind.Frontier, priority, _ =>
        {
            var config = new AnthropicConfig();
            configure(config);
            var generator = new AnthropicMessageGenerator(config);
            return new ChatClientAdapter(generator, modelId, "anthropic");
        });
    }

    /// <summary>
    /// Registers an ironhive GoogleAI provider as a <see cref="ProviderKind.Frontier"/> candidate.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="modelId">Default model id forwarded to the chat client metadata.</param>
    /// <param name="configure">Action to configure <see cref="GoogleAIConfig"/> (API key, HTTP options, etc.).</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IronProwBuilder AddIronHiveGoogleAI(
        this IronProwBuilder builder,
        string id,
        int priority,
        string modelId,
        Action<GoogleAIConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddProvider(id, ProviderKind.Frontier, priority, _ =>
        {
            var config = new GoogleAIConfig();
            configure(config);
            var generator = new GoogleAIMessageGenerator(config);
            return new ChatClientAdapter(generator, modelId, "googleai");
        });
    }

    /// <summary>
    /// Registers an ironhive GPUStack provider (OpenAI-compatible, served over the LAN) as a
    /// <see cref="ProviderKind.Lan"/> candidate. GPUStack auth is key-optional, so a configured
    /// <see cref="GpuStackConfig.ApiKey"/> is supported but not required.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="modelId">Default model id forwarded to the chat client metadata.</param>
    /// <param name="configure">Action to configure <see cref="GpuStackConfig"/> (base URL, optional API key, connect timeout).</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IronProwBuilder AddIronHiveGpuStack(
        this IronProwBuilder builder,
        string id,
        int priority,
        string modelId,
        Action<GpuStackConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddProvider(id, ProviderKind.Lan, priority, _ =>
        {
            var config = new GpuStackConfig();
            configure(config);
            var generator = new GpuStackMessageGenerator(config);
            return new ChatClientAdapter(generator, modelId, "gpustack");
        });
    }

    /// <summary>
    /// Registers an ironhive generic OpenAI-compatible provider (Ollama, LM Studio, vLLM, llama.cpp
    /// server, etc.) as a <see cref="ProviderKind.Lan"/> candidate. These endpoints expose the
    /// conventional <c>/v1</c> OpenAI API surface and treat the API key as optional, matching LAN
    /// services that accept unauthenticated requests. Defaults to Ollama's <c>http://localhost:11434</c>.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="modelId">Default model id forwarded to the chat client metadata.</param>
    /// <param name="configure">Action to configure <see cref="OpenAICompatibleConfig"/> (base URL, optional API key, path, connect timeout).</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IronProwBuilder AddIronHiveOpenAICompatible(
        this IronProwBuilder builder,
        string id,
        int priority,
        string modelId,
        Action<OpenAICompatibleConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.AddProvider(id, ProviderKind.Lan, priority, _ =>
        {
            var config = new OpenAICompatibleConfig();
            configure(config);
            var generator = new OpenAICompatibleMessageGenerator(config);
            return new ChatClientAdapter(generator, modelId, "openai-compatible");
        });
    }
}
