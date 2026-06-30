namespace IronProw.Core;

/// <summary>Attempts candidates in registry priority order (highest priority first).</summary>
public sealed class DefaultProviderSelector : IProviderSelector
{
    /// <inheritdoc />
    public IReadOnlyList<ProviderRegistration> Order(ChatSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Candidates;
    }
}
