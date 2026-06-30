using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>Provider-agnostic input/output guardrail.</summary>
public interface IGuard
{
    /// <summary>Inspects request messages before they reach the provider.</summary>
    ValueTask<GuardVerdict> InspectInputAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct);

    /// <summary>Inspects the provider response before it returns to the caller.</summary>
    ValueTask<GuardVerdict> InspectOutputAsync(ChatResponse response, CancellationToken ct);
}
