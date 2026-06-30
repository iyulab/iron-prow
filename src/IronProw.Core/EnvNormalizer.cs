namespace IronProw.Core;

/// <summary>
/// Normalizes provider environment variables to canonical keys. Never emits values to logs
/// (API-key boundary): callers receive a dictionary; values are not traced.
/// </summary>
public static class EnvNormalizer
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["GPUSTACK_KEY"] = "GPUSTACK_API_KEY",
        ["GPUSTACK_URL"] = "GPUSTACK_BASE_URL",
        ["OPENAI_KEY"] = "OPENAI_API_KEY",
        ["OPENAI_URL"] = "OPENAI_BASE_URL",
        ["ANTHROPIC_KEY"] = "ANTHROPIC_API_KEY",
    };

    /// <summary>Maps aliases to canonical keys, trims values, drops null/empty.</summary>
    public static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string?> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            var canonical = Aliases.TryGetValue(key, out var mapped) ? mapped : key;
            result[canonical] = trimmed;
        }
        return result;
    }
}
