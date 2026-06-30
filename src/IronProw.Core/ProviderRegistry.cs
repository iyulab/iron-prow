namespace IronProw.Core;

/// <inheritdoc />
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<string, (ProviderRegistration Reg, int Seq)> _items = new(StringComparer.Ordinal);
    private int _seq;

    /// <inheritdoc />
    public void Register(ProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _items[registration.Id] = (registration, _seq++);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderRegistration> GetOrdered()
        => _items.Values
            .OrderByDescending(x => x.Reg.Priority)
            .ThenBy(x => x.Seq)
            .Select(x => x.Reg)
            .ToList();
}
