namespace IronProw.Core;

/// <summary>Thrown when a guardrail blocks a request or response.</summary>
public sealed class GuardException : Exception
{
    /// <summary>The block reason from the guard verdict.</summary>
    public string Reason { get; }

    /// <summary>Creates a guard exception with the given block reason.</summary>
    public GuardException(string reason) : base($"Guard blocked the call: {reason}")
        => Reason = reason;
}
