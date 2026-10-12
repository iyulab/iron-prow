using System.Reflection;
using Iyu.Conventions.Testing.Snippets;
using Xunit;

namespace IronProw.Architecture.Tests;

/// <summary>
/// Compiles every C# block in README.md against the current assemblies. The README once resolved its client from a
/// <c>host</c> it never declared, took its local generator and readiness probe from comments, and named an options type
/// that does not exist — nothing checked any of it. A compiler checks the receiver, the arguments, the members read and
/// the namespaces.
/// </summary>
/// <remarks>
/// Each block is compiled as a top-level program by <see cref="SnippetCompiler"/>: its own usings, the usings below, and
/// the stand-ins below when the block uses the name without declaring it — values a reader already has from the
/// surrounding text (the service collection, a request scope), not part of what the block shows.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    private static readonly IReadOnlyList<DocSnippet> Snippets = DocSnippets.Read(ReadmePath());

    // A block that is deliberately not a program (a signature sketch, pseudocode), by the heading it sits under, with the
    // reason. Shrink this, never grow it silently.
    private static readonly SnippetRoster Fragments = new(new Dictionary<string, string>(StringComparer.Ordinal));

    private static readonly SnippetCompiler Compiler = new(SnippetCompiler.ReferencesFor(
        [.. new[]
        {
            "IronProw.Core", "IronProw.FluxGuard", "IronProw.IronHive", "IronProw.LMSupply",
            "LMSupply.Generator", "LMSupply.Core", "FluxGuard",
            "Microsoft.Extensions.AI", "Microsoft.Extensions.AI.Abstractions",
            "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Caching.Abstractions",
        }.Select(Assembly.Load)]))
    {
        Usings =
        [
            "System", "System.Collections.Generic", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks",
            "IronProw.Core", "IronProw.FluxGuard", "IronProw.IronHive", "IronProw.LMSupply",
            "LMSupply.Generator", "LMSupply.Generator.Abstractions",
            "Microsoft.Extensions.AI", "Microsoft.Extensions.DependencyInjection",
        ],
        StandIns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["services"] = "IServiceCollection services = null!;",
            ["sp"] = "IServiceProvider sp = null!;",
            ["scopedSp"] = "IServiceProvider scopedSp = null!;",
            ["workspaceId"] = "string workspaceId = \"\";",
            ["messages"] = "List<ChatMessage> messages = [];",
            ["options"] = "ChatOptions? options = null;",
            ["ct"] = "CancellationToken ct = default;",
            ["inner"] = "IChatClient inner = null!;",
            ["generator"] = "ITextGenerator generator = null!;",
            ["probe"] = "IReadinessProbe probe = null!;",
            ["distributedCache"] = "Microsoft.Extensions.Caching.Distributed.IDistributedCache distributedCache = null!;",
            ["tenantId"] = "string tenantId = \"\";",
        },
    };

    public static TheoryData<string> Blocks() => new(Snippets.Select(s => s.Key));

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var snippet = Snippets.Single(s => s.Key == key);
        if (!Fragments.IsFragment(snippet))
            Compiler.ShouldCompile(snippet);
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndFragmentsNameRealHeadings() => Fragments.ShouldMatch([.. Snippets], atLeast: 9);

    /// <summary>Positive control: the compiler rejects what the old Quick Start did.</summary>
    [Fact]
    public void Compile_RejectsTheOldQuickStart()
    {
        var errors = Compiler.Compile("""
            IChatClient chat = host.Services.GetRequiredService<IChatClient>();
            IChatClient piped = chat.WithDegenerationStop(o => o.TailWindow = 240);
            """);

        Assert.NotEmpty(errors);
    }

    private static string ReadmePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IronProw.slnx")))
            dir = dir.Parent;
        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("IronProw.slnx not found above the test output directory"),
            "README.md");
    }
}
