namespace IronProw.Core;

/// <summary>Orders provider candidates into attempt order (head tried first, rest are fallbacks).</summary>
public interface IProviderSelector
{
    /// <summary>Returns the candidates in the order they should be attempted.</summary>
    IReadOnlyList<ProviderRegistration> Order(ChatSelectionContext context);
}
