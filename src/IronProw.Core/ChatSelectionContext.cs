using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>Inputs available to a provider selection decision.</summary>
public sealed record ChatSelectionContext(
    IReadOnlyList<ChatMessage> Messages,
    ChatOptions? Options,
    IReadOnlyList<ProviderRegistration> Candidates);
