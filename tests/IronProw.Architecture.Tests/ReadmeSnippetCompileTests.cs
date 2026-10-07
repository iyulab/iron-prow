using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronProw.Architecture.Tests;

/// <summary>
/// Compiles every <c>```csharp</c> block in README.md against the current assemblies. The README once resolved its
/// client from a <c>host</c> it never declared, took its local generator and readiness probe from comments, and named
/// an options type that does not exist — nothing checked any of it. A compiler checks the receiver, the arguments,
/// the members read and the namespaces.
/// </summary>
/// <remarks>
/// A block is compiled as a top-level program: its <c>using</c> lines are hoisted, the common usings below are added, and
/// the stand-ins below are declared when the block uses the name without declaring it — values a reader already has
/// from the surrounding text (the service collection, a request scope), not part of what the block shows.
/// </remarks>
public class ReadmeSnippetCompileTests
{
    // A block that is deliberately not a program (a signature sketch, pseudocode) is listed here by the heading it sits
    // under, with the reason. Shrink this, never grow it silently.
    private static readonly Dictionary<string, string> Fragments = new(StringComparer.Ordinal);

    private const string CommonUsings = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using IronProw.Core;
        using IronProw.FluxGuard;
        using IronProw.IronHive;
        using IronProw.LMSupply;
        using LMSupply.Generator;
        using LMSupply.Generator.Abstractions;
        using Microsoft.Extensions.AI;
        using Microsoft.Extensions.DependencyInjection;
        """;

    private static readonly (string Name, string Declaration)[] StandIns =
    [
        ("services", "IServiceCollection services = null!;"),
        ("sp", "IServiceProvider sp = null!;"),
        ("scopedSp", "IServiceProvider scopedSp = null!;"),
        ("workspaceId", "string workspaceId = \"\";"),
        ("messages", "List<ChatMessage> messages = [];"),
        ("options", "ChatOptions? options = null;"),
        ("ct", "CancellationToken ct = default;"),
        ("inner", "IChatClient inner = null!;"),
        ("generator", "ITextGenerator generator = null!;"),
        ("probe", "IReadinessProbe probe = null!;"),
        ("distributedCache", "Microsoft.Extensions.Caching.Distributed.IDistributedCache distributedCache = null!;"),
        ("tenantId", "string tenantId = \"\";"),
    ];

    private static readonly string[] AssembliesToLoad =
    [
        "IronProw.Core", "IronProw.FluxGuard", "IronProw.IronHive", "IronProw.LMSupply",
        "LMSupply.Generator", "LMSupply.Core", "FluxGuard",
        "Microsoft.Extensions.AI", "Microsoft.Extensions.AI.Abstractions",
        "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Caching.Abstractions",
    ];

    public static TheoryData<string> Blocks()
    {
        var data = new TheoryData<string>();
        foreach (var block in ReadBlocks())
            data.Add(block.Key);
        return data;
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ReadmeBlock_Compiles(string key)
    {
        var block = ReadBlocks().Single(b => b.Key == key);
        if (Fragments.ContainsKey(block.Heading))
            return;

        var errors = Compile(block.Code);

        Assert.True(errors.IsEmpty,
            $"README block {key} does not compile against the current API:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- source ---\n" + Program(block.Code));
    }

    [Fact]
    public void EveryReadmeBlock_IsFoundAndFragmentsNameRealHeadings()
    {
        var blocks = ReadBlocks();
        Assert.True(blocks.Count >= 9, $"expected the README's C# blocks, found {blocks.Count}");
        Assert.All(Fragments.Keys, heading => Assert.Contains(blocks, b => b.Heading == heading));
    }

    /// <summary>Positive control: the compiler rejects what the old Quick Start did.</summary>
    [Fact]
    public void Compile_RejectsTheOldQuickStart()
    {
        var errors = Compile("""
            IChatClient chat = host.Services.GetRequiredService<IChatClient>();
            IChatClient piped = chat.WithDegenerationStop(o => o.TailWindow = 240);
            """);

        Assert.NotEmpty(errors);
    }

    private sealed record Block(string Key, string Heading, string Code);

    private static List<Block> ReadBlocks()
    {
        var lines = File.ReadAllText(ReadmePath()).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<Block>();
        var heading = "(top)";
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#'))
                heading = lines[i].TrimStart('#').Trim();
            if (lines[i].Trim() != "```csharp")
                continue;

            var start = i + 1;
            var code = new StringBuilder();
            for (i++; i < lines.Length && lines[i].Trim() != "```"; i++)
                code.AppendLine(lines[i]);
            blocks.Add(new Block($"line {start}: {heading}", heading, code.ToString()));
        }

        return blocks;
    }

    private static string Program(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        // "using X;   // what it is for" is a directive too.
        static string Code(string l) => l.Split("//", 2)[0].TrimEnd();
        bool IsUsingDirective(string l) =>
            l.StartsWith("using ", StringComparison.Ordinal) && Code(l).EndsWith(';') && !l.StartsWith("using var ", StringComparison.Ordinal);

        var body = string.Join("\n", lines.Where(l => !IsUsingDirective(l)));
        var standIns = StandIns
            .Where(s => Regex.IsMatch(body, $@"\b{s.Name}\b")
                        && !Regex.IsMatch(body, $@"\b(var|[A-Z][\w<>?,\s]*)\s+{s.Name}\s*[=;]"))
            .Select(s => s.Declaration);

        return string.Join("\n", lines.Where(IsUsingDirective)) + "\n" + CommonUsings + "\n"
               + string.Join("\n", standIns) + "\n" + body;
    }

    private static ImmutableArray<Diagnostic> Compile(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(Program(code), new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "ReadmeSnippet", [tree], References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
    }

    private static List<MetadataReference> References()
    {
        foreach (var name in AssembliesToLoad)
            Assembly.Load(name);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            paths.UnionWith(trusted.Split(Path.PathSeparator).Where(p => p.Length > 0));
        paths.UnionWith(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => a.Location));
        return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
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
