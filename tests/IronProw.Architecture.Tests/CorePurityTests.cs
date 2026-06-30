using System.Reflection;
using FluentAssertions;
using IronProw.Core;
using Xunit;

namespace IronProw.Architecture.Tests;

public class CorePurityTests
{
    // Forbidden: iyulab implementation assemblies must never be in IronProw.Core's closure.
    private static readonly string[] ForbiddenPrefixes =
        ["IronHive.", "FluxGuard", "FluxIndex", "LMSupply", "FileFlux", "WebFlux", "IronProw.IronHive", "IronProw.FluxGuard", "IronProw.LMSupply"];

    [Fact]
    public void Core_does_not_reference_iyulab_implementations()
    {
        var core = typeof(SelectingChatClient).Assembly;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();
        queue.Enqueue(core);
        var offenders = new List<string>();

        while (queue.Count > 0)
        {
            var asm = queue.Dequeue();
            foreach (var refName in asm.GetReferencedAssemblies())
            {
                if (!seen.Add(refName.Name!)) continue;
                if (ForbiddenPrefixes.Any(p => refName.Name!.StartsWith(p, StringComparison.Ordinal)))
                    offenders.Add($"{asm.GetName().Name} -> {refName.Name}");
                try { queue.Enqueue(Assembly.Load(refName)); } catch { /* transitive not loadable in test host; name check already done */ }
            }
        }

        offenders.Should().BeEmpty("IronProw.Core must stay free of iyulab implementation assemblies (M4-1)");
    }
}
