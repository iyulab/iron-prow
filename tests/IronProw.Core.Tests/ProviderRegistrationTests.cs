using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

public class ProviderRegistrationTests
{
    [Fact]
    public void Registration_exposes_id_kind_priority_and_factory()
    {
        var client = Substitute.For<IChatClient>();
        var reg = new ProviderRegistration("gpustack", ProviderKind.Lan, 100, _ => client);

        reg.Id.Should().Be("gpustack");
        reg.Kind.Should().Be(ProviderKind.Lan);
        reg.Priority.Should().Be(100);
        reg.ClientFactory(Substitute.For<IServiceProvider>()).Should().BeSameAs(client);
    }
}
