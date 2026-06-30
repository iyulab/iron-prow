using FluxGuard.Core;
using IronProw.Core;
using Microsoft.Extensions.AI;

namespace IronProw.FluxGuard;

/// <summary>
/// <see cref="IGuard"/> implementation backed by <see cref="global::FluxGuard.IFluxGuard"/>.
/// </summary>
/// <remarks>
/// Verdict mapping:
/// <list type="bullet">
///   <item><c>Blocked</c> → <see cref="GuardVerdict.Block(string)"/> using <c>BlockReason</c>.</item>
///   <item><c>Flagged</c> or <c>NeedsEscalation</c> → <see cref="GuardVerdict.Block(string)"/> using
///     the first triggered guard's <c>Details</c> (fail-closed; an unresolved escalation must not pass
///     through a safe-inference gateway).</item>
///   <item><c>Pass</c> → <see cref="GuardVerdict.Allow()"/>.</item>
/// </list>
/// </remarks>
public sealed class FluxGuardGuard : IGuard
{
    private readonly global::FluxGuard.IFluxGuard _guard;

    /// <summary>Initializes a new <see cref="FluxGuardGuard"/> wrapping the supplied <paramref name="guard"/>.</summary>
    public FluxGuardGuard(global::FluxGuard.IFluxGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _guard = guard;
    }

    /// <inheritdoc />
    public async ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var text = string.Join("\n", messages.Select(m => m.Text ?? string.Empty));
        var result = await _guard.CheckInputAsync(text, ct).ConfigureAwait(false);
        return ToVerdict(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The original request input is not available to this method per the <see cref="IGuard"/> contract.
    /// FluxGuard's output inspection is therefore called with an empty input context, which may limit
    /// input-aware output rules. Output rules that do not require request context operate normally.
    /// </remarks>
    public async ValueTask<GuardVerdict> InspectOutputAsync(ChatResponse response, CancellationToken ct)
    {
        var text = response.Text ?? string.Empty;
        var result = await _guard.CheckOutputAsync(string.Empty, text, ct).ConfigureAwait(false);
        return ToVerdict(result);
    }

    private static GuardVerdict ToVerdict(GuardResult result)
    {
        if (result.IsBlocked)
            return GuardVerdict.Block(result.BlockReason ?? "Content blocked by FluxGuard");

        if (result.IsFlagged || result.NeedsEscalation)
        {
            var reason = result.TriggeredGuards.Count > 0
                ? result.TriggeredGuards[0].Details ?? "Content flagged by FluxGuard"
                : "Content flagged by FluxGuard";
            return GuardVerdict.Block(reason);
        }

        return GuardVerdict.Allow();
    }
}
