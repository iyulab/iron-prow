using FluentAssertions;
using Xunit;

namespace IronProw.Core.Tests;

public class EnvNormalizerTests
{
    [Fact]
    public void Normalize_maps_aliases_to_canonical_keys()
    {
        var raw = new Dictionary<string, string?>
        {
            ["GPUSTACK_KEY"] = " abc ",     // alias of GPUSTACK_API_KEY, trimmed
            ["OPENAI_API_KEY"] = "sk-1",
            ["UNKNOWN"] = "x"
        };

        var result = EnvNormalizer.Normalize(raw);

        result["GPUSTACK_API_KEY"].Should().Be("abc");
        result["OPENAI_API_KEY"].Should().Be("sk-1");
        result["UNKNOWN"].Should().Be("x");
    }

    [Fact]
    public void Normalize_drops_null_and_empty()
    {
        var raw = new Dictionary<string, string?> { ["OPENAI_API_KEY"] = "", ["ANTHROPIC_API_KEY"] = null };
        EnvNormalizer.Normalize(raw).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_prefers_explicit_canonical_over_alias()
    {
        var raw = new Dictionary<string, string?>
        {
            ["GPUSTACK_KEY"] = "from-alias",
            ["GPUSTACK_API_KEY"] = "from-canonical"
        };
        EnvNormalizer.Normalize(raw)["GPUSTACK_API_KEY"].Should().Be("from-canonical");
    }
}
