using FluentAssertions;
using Xunit;

namespace IronProw.Core.Tests;

public class GuardVerdictTests
{
    [Fact]
    public void Allow_is_allowed_with_no_reason()
    {
        var v = GuardVerdict.Allow();
        v.Allowed.Should().BeTrue();
        v.Reason.Should().BeNull();
    }

    [Fact]
    public void Block_carries_reason()
    {
        var v = GuardVerdict.Block("prompt injection");
        v.Allowed.Should().BeFalse();
        v.Reason.Should().Be("prompt injection");
    }

    [Fact]
    public void GuardException_exposes_reason()
    {
        var ex = new GuardException("jailbreak");
        ex.Reason.Should().Be("jailbreak");
    }
}
