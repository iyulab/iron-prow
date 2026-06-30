using FluxGuard.Presets;
using IronProw.Core;

namespace IronProw.FluxGuard;

/// <summary>Extension methods to register FluxGuard as the iron-prow guardrail.</summary>
public static class FluxGuardExtensions
{
    /// <summary>
    /// Registers a <see cref="FluxGuardGuard"/> backed by the supplied <paramref name="guard"/> instance.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="guard">The underlying FluxGuard instance.</param>
    /// <param name="failMode">
    /// How uncertain verdicts (Flagged / NeedsEscalation) are resolved. Defaults to
    /// <see cref="FluxGuardFailMode.Closed"/> (the safe default).
    /// </param>
    public static IronProwBuilder UseFluxGuard(
        this IronProwBuilder builder,
        global::FluxGuard.IFluxGuard guard,
        FluxGuardFailMode failMode = FluxGuardFailMode.Closed)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(guard);
        return builder.UseGuard(_ => new FluxGuardGuard(guard, failMode));
    }

    /// <summary>
    /// Registers a <see cref="FluxGuardGuard"/> built with the Standard preset (L1 regex guards, offline).
    /// The optional <paramref name="configure"/> action can add or override settings after the preset is applied.
    /// </summary>
    /// <param name="builder">The iron-prow builder.</param>
    /// <param name="configure">Optional action to add or override settings after the preset is applied.</param>
    /// <param name="failMode">
    /// How uncertain verdicts (Flagged / NeedsEscalation) are resolved. Defaults to
    /// <see cref="FluxGuardFailMode.Closed"/> (the safe default).
    /// </param>
    public static IronProwBuilder UseFluxGuard(
        this IronProwBuilder builder,
        Action<global::FluxGuard.FluxGuardBuilder>? configure = null,
        FluxGuardFailMode failMode = FluxGuardFailMode.Closed)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var guard = global::FluxGuard.FluxGuard.Create(b =>
        {
            b.ApplyStandardPreset();
            configure?.Invoke(b);
        });
        return builder.UseGuard(_ => new FluxGuardGuard(guard, failMode));
    }
}
