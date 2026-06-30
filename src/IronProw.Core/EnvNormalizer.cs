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

    /// <summary>
    /// Maps aliases to canonical keys, trims values, drops null/empty.
    /// When both an alias and its canonical key are provided, the explicit canonical value wins.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string?> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // First pass: assign all non-alias keys (canonical or unknown)
        foreach (var (key, value) in raw)
        {
            // Skip if this key is an alias
            if (Aliases.ContainsKey(key))
                continue;

            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            result[key] = trimmed;
        }

        // Second pass: assign alias keys only if their canonical target is not already present
        foreach (var (key, value) in raw)
        {
            // Skip if this key is not an alias
            if (!Aliases.TryGetValue(key, out var canonical))
                continue;

            // Skip if the canonical key is already present
            if (result.ContainsKey(canonical))
                continue;

            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            result[canonical] = trimmed;
        }

        return result;
    }
}
