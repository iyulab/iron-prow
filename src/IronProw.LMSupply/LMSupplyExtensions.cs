using IronProw.Core;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;

namespace IronProw.LMSupply;

/// <summary>
/// Extension methods to register an lm-supply local inference provider as an iron-prow gateway candidate.
/// </summary>
public static class LMSupplyExtensions
{
    /// <summary>
    /// Registers a <see cref="ProviderKind.Local"/> candidate backed by a raw lm-supply
    /// <see cref="ITextGenerator"/>. The generator is bridged to <see cref="IChatClient"/> via
    /// <see cref="GeneratorChatClient"/> and wrapped in a <see cref="LocalSafetyChatClient"/>, so
    /// consumers (e.g. textree) need only supply the loaded generator — no hand-rolled bridge.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="generator">
    /// A pre-loaded lm-supply generator. Its lifecycle (load/dispose) is owned by the caller; the
    /// gateway never disposes it.
    /// </param>
    /// <param name="probe">Readiness and model-availability probe for the local provider.</param>
    /// <param name="options">Safety options; defaults are used when <see langword="null"/>.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <remarks>
    /// This sets up the full gateway stack (registry, selection, resilience). A consumer with a
    /// <em>single</em> local provider and no second fallback candidate does not need those layers —
    /// for that case prefer the lightweight
    /// <see cref="BuildLocalSafeClient(ITextGenerator, IReadinessProbe, LocalSafetyOptions?)"/>, which
    /// returns a guarded local <see cref="IChatClient"/> without any builder or registry.
    /// </remarks>
    public static IronProwBuilder AddLMSupplyLocal(
        this IronProwBuilder builder,
        string id,
        int priority,
        ITextGenerator generator,
        IReadinessProbe probe,
        LocalSafetyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(probe);
        var safetyOptions = options ?? new LocalSafetyOptions();
        return builder.AddProvider(id, ProviderKind.Local, priority,
            _ => new LocalSafetyChatClient(new GeneratorChatClient(generator), safetyOptions, probe));
    }

    /// <summary>
    /// Registers a <see cref="ProviderKind.Local"/> candidate whose <see cref="IChatClient"/> is
    /// <paramref name="rawClient"/> wrapped in a <see cref="LocalSafetyChatClient"/>.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="id">Unique provider id for the gateway registry.</param>
    /// <param name="priority">Selection priority — higher wins.</param>
    /// <param name="rawClient">
    /// Pre-bridged <see cref="IChatClient"/> backed by lm-supply. Prefer the
    /// <see cref="AddLMSupplyLocal(IronProwBuilder, string, int, ITextGenerator, IReadinessProbe, LocalSafetyOptions?)"/>
    /// overload, which bridges a raw <see cref="ITextGenerator"/> via <see cref="GeneratorChatClient"/> and
    /// removes the need for consumers to hand-roll the bridge. This overload remains for callers that
    /// already hold a bridged client (e.g. ironhive-host's own adapter).
    /// </param>
    /// <param name="probe">Readiness and model-availability probe for the local provider.</param>
    /// <param name="options">Safety options; defaults are used when <see langword="null"/>.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IronProwBuilder AddLMSupplyLocal(
        this IronProwBuilder builder,
        string id,
        int priority,
        IChatClient rawClient,
        IReadinessProbe probe,
        LocalSafetyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(rawClient);
        ArgumentNullException.ThrowIfNull(probe);
        var safetyOptions = options ?? new LocalSafetyOptions();
        return builder.AddProvider(id, ProviderKind.Local, priority,
            _ => new LocalSafetyChatClient(rawClient, safetyOptions, probe));
    }

    /// <summary>
    /// Builds a guarded local <see cref="IChatClient"/> for a <em>single</em> lm-supply provider,
    /// without any gateway. The raw <see cref="ITextGenerator"/> is bridged via
    /// <see cref="GeneratorChatClient"/> and wrapped in a <see cref="LocalSafetyChatClient"/>
    /// (readiness gate + length-bounding), returning a plain <see cref="IChatClient"/>.
    /// <para>
    /// Use this when a consumer runs local-first with no second fallback candidate: the gateway's
    /// registry / selection / resilience layers would be inert. When multiple providers, priority
    /// selection, or provider-level fallback are needed, use
    /// <see cref="AddLMSupplyLocal(IronProwBuilder, string, int, ITextGenerator, IReadinessProbe, LocalSafetyOptions?)"/>
    /// instead.
    /// </para>
    /// </summary>
    /// <param name="generator">
    /// A pre-loaded lm-supply generator. Its lifecycle (load/dispose) is owned by the caller.
    /// </param>
    /// <param name="probe">Readiness and model-availability probe for the local provider.</param>
    /// <param name="options">Safety options; defaults are used when <see langword="null"/>.</param>
    /// <returns>A guarded local <see cref="IChatClient"/> ready to use directly.</returns>
    public static IChatClient BuildLocalSafeClient(
        ITextGenerator generator,
        IReadinessProbe probe,
        LocalSafetyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(probe);
        return new LocalSafetyChatClient(new GeneratorChatClient(generator), options ?? new LocalSafetyOptions(), probe);
    }
}
