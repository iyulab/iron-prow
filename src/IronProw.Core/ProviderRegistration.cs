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
    Func<IServiceProvider, IChatClient> ClientFactory);
