namespace IronProw.Core;

/// <summary>Outcome of a guardrail inspection.</summary>
public readonly record struct GuardVerdict(bool Allowed, string? Reason)
{
    /// <summary>An allowing verdict.</summary>
    public static GuardVerdict Allow() => new(true, null);

    /// <summary>A blocking verdict with a human-readable reason.</summary>
    public static GuardVerdict Block(string reason) => new(false, reason);
}
