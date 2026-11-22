using Microsoft.Extensions.AI;

namespace IronProw.Core;

/// <summary>
/// A registered inference provider candidate. Higher <see cref="Priority"/> is preferred.
/// </summary>
/// <param name="Id">Stable unique identifier (e.g. "gpustack", "openai").</param>
/// <param name="Kind">
/// Deployment locus — metadata for a custom <see cref="IProviderSelector"/> (for example "local first when the
/// request is short"). The default selector orders by <paramref name="Priority"/> only and does not read it.
/// </param>
/// <param name="Priority">Selection priority; higher wins under the default selector.</param>
/// <param name="ClientFactory">Builds the raw provider <see cref="IChatClient"/> from DI.</param>
public sealed record ProviderRegistration(
    string Id,
    ProviderKind Kind,
    int Priority,
    Func<IServiceProvider, IChatClient> ClientFactory)
{
    /// <summary>
    /// Model names this candidate serves besides its client's default model (case-sensitive), or null when it
    /// declares none. A request's <see cref="ChatOptions.ModelId"/> names the model of the candidate the request
    /// was meant for; when the gateway falls back to another candidate it sends the request without that name -
    /// so the candidate's own default model answers - unless the name is listed here. A multi-model server reached by
    /// name must list the names it serves, or a fallback onto it is answered by its default model. Set it here (a
    /// tenant resolver) or with <see cref="IronProwBuilder.ServeModels"/>. See <see cref="IronProwOptions.FallbackKeepsModelId"/>.
    /// </summary>
    public IReadOnlyCollection<string>? ServedModels { get; init; }
}
