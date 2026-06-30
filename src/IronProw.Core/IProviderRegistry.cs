namespace IronProw.Core;

/// <summary>Holds provider candidates for selection.</summary>
public interface IProviderRegistry
{
    /// <summary>Registers (or replaces by <see cref="ProviderRegistration.Id"/>) a candidate.</summary>
    void Register(ProviderRegistration registration);

    /// <summary>Returns candidates ordered by priority (highest first), ties by insertion order.</summary>
    IReadOnlyList<ProviderRegistration> GetOrdered();
}
