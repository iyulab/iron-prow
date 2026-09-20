using System.Reflection;
using Iyu.Conventions.Testing;
using Xunit;

namespace IronProw.Architecture.Tests;

/// <summary>
/// Every public option in this library is read by the library. An option nothing reads is a promise it does not keep:
/// a caller sets it, and nothing changes and nothing is reported. The roster fails both ways — a new unread option,
/// and a listed one that has since been wired — so each change is recorded on purpose.
/// </summary>
public class OptionsReachabilityRosterTests
{
    private static readonly Assembly[] Libraries =
    [
        // Every assembly this repository ships. An option declared in one and read in another only counts as
        // read when both are scanned, which is why this lives in the one test project that references them all.
        Assembly.Load("IronProw.Core"),
        Assembly.Load("IronProw.FluxGuard"),
        Assembly.Load("IronProw.IronHive"),
        Assembly.Load("IronProw.LMSupply"),
    ];

    /// <summary>Options accepted as unread today, each with the reason. Shrink this list; never grow it silently.</summary>
    private static readonly Dictionary<string, string[]> KnownUnread = new()
    {
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options", "Config"))
            .ShouldMatchRoster(KnownUnread);
}
