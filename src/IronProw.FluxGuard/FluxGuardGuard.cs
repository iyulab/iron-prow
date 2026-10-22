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
///   <item><c>Flagged</c> or <c>NeedsEscalation</c> → blocked when <see cref="FluxGuardFailMode.Closed"/>
///     (default) using the first triggered guard's <c>Details</c>; allowed when
///     <see cref="FluxGuardFailMode.Open"/> (opt-in).</item>
///   <item>Unchecked input parts (<c>UncheckedParts</c> &gt; 0) → blocked when <see cref="FluxGuardFailMode.Closed"/>;
///     allowed when <see cref="FluxGuardFailMode.Open"/>.</item>
///   <item><c>Pass</c> → <see cref="GuardVerdict.Allow()"/>.</item>
/// </list>
/// A definite <c>Blocked</c> result always blocks regardless of <see cref="FluxGuardFailMode"/>.
/// </remarks>
public sealed class FluxGuardGuard : IGuard
{
    private readonly global::FluxGuard.IFluxGuard _guard;
    private readonly FluxGuardFailMode _failMode;

    /// <summary>
    /// Initializes a new <see cref="FluxGuardGuard"/> wrapping the supplied <paramref name="guard"/>.
    /// </summary>
    /// <param name="guard">The underlying FluxGuard instance.</param>
    /// <param name="failMode">
    /// How uncertain verdicts (Flagged / NeedsEscalation) are resolved. Defaults to
    /// <see cref="FluxGuardFailMode.Closed"/> (block — the safe default for a safe-inference gateway).
    /// </param>
    public FluxGuardGuard(global::FluxGuard.IFluxGuard guard, FluxGuardFailMode failMode = FluxGuardFailMode.Closed)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _guard = guard;
        _failMode = failMode;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The text of every request message is judged together with the inline non-text contents of every message
    /// (<see cref="DataContent"/> — an attached image) as <see cref="GuardContext.Parts"/>: FluxGuard reads a part through
    /// the <c>IInputPartTextExtractor</c> registered on the guard (OCR, or a transcribe-only vision call), so an
    /// instruction written inside an image is judged like the same words typed. A part referenced by URI only
    /// (<see cref="UriContent"/>) has no bytes here and is not passed. A part the guard could not read
    /// (<see cref="GuardResult.UncheckedParts"/>) leaves the decision covering less than the model will see — it is
    /// blocked in <see cref="FluxGuardFailMode.Closed"/> and allowed in <see cref="FluxGuardFailMode.Open"/>.
    /// </remarks>
    public async ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var text = string.Join("\n", messages.Select(m => m.Text ?? string.Empty));
        var parts = messages
            .SelectMany(m => m.Contents.OfType<DataContent>())
            .Where(d => !d.Data.IsEmpty)
            .Select(d => new GuardInputPart(d.MediaType, d.Data))
            .ToList();
        var result = await _guard.CheckInputAsync(new GuardContext
        {
            OriginalInput = text,
            Parts = parts,
            CancellationToken = ct
        }).ConfigureAwait(false);
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

    private GuardVerdict ToVerdict(GuardResult result)
    {
        // A definite block is a clear policy decision — it always blocks, independent of fail mode.
        if (result.IsBlocked)
            return GuardVerdict.Block(result.BlockReason ?? "Content blocked by FluxGuard");

        // Parts the guard could not read are uncertain coverage — the fail mode decides, as for an uncertain verdict.
        if (result.UncheckedParts > 0 && _failMode == FluxGuardFailMode.Closed)
            return GuardVerdict.Block(
                $"{result.UncheckedParts} input part(s) could not be checked (no text extractor read them); blocked in fail-closed mode");

        // Flagged / NeedsEscalation are uncertain verdicts — the fail mode decides their resolution.
        if (result.IsFlagged || result.NeedsEscalation)
        {
            if (_failMode == FluxGuardFailMode.Open)
                return GuardVerdict.Allow();

            var reason = result.TriggeredGuards.Count > 0
                ? result.TriggeredGuards[0].Details ?? "Content flagged by FluxGuard"
                : "Content flagged by FluxGuard";
            return GuardVerdict.Block(reason);
        }

        return GuardVerdict.Allow();
    }
}
