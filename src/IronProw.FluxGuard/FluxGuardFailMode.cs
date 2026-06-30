namespace IronProw.FluxGuard;

/// <summary>
/// Controls how <see cref="FluxGuardGuard"/> resolves <em>uncertain</em> verdicts — i.e. FluxGuard
/// results that are <c>Flagged</c> or <c>NeedsEscalation</c> rather than a definite block.
/// A definite <c>Blocked</c> result always blocks regardless of this mode.
/// </summary>
public enum FluxGuardFailMode
{
    /// <summary>
    /// Uncertain verdicts block (safe default). An unresolved escalation must not pass through a
    /// safe-inference gateway.
    /// </summary>
    Closed,

    /// <summary>
    /// Uncertain verdicts pass through (opt-in). For consumers that prefer availability over strict
    /// gating and resolve flagged content out of band (e.g. Filer's <c>FailMode.Open</c>).
    /// </summary>
    Open
}
