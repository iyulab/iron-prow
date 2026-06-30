using IronHive.Core.Compatibility;
using IronHive.Providers.Anthropic;
using IronHive.Providers.GoogleAI;
using IronHive.Providers.OpenAI;
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
            var config = new OpenAIConfig();
            configure(config);
            var generator = new OpenAIMessageGenerator(config);
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
}
