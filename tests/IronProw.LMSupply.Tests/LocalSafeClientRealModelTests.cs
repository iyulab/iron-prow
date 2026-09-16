using AwesomeAssertions;
using IronProw.LMSupply;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Xunit;

namespace IronProw.LMSupply.Tests;

/// <summary>
/// The two guarantees of the 0.4.0 local path, against a real thinking-default model rather than a fake
/// generator: a length-bounded call returns an answer instead of an empty string, and the response carries
/// the model id and the token usage the generator reported.
/// </summary>
/// <remarks>
/// <para>
/// Why a real model is needed at all: the defect (docket-tracked as the local path's empty-answer failure)
/// only exists because a thinking-default model spends a small output budget on reasoning before it writes
/// any answer text. A fake generator cannot reproduce that — it returns whatever the test told it to — so the
/// unit facts pin the <em>mapping</em> (effort → thinking mode, reasoning → <c>TextReasoningContent</c>) while
/// these pin the <em>outcome</em>. Reverting <c>LocalSafetyOptions.DefaultReasoningEffort</c> to null makes the
/// first fact fail, which is what the control fact demonstrates in the same run.
/// </para>
/// <para>
/// Excluded from CI (<c>Category=RequiresGpu</c>): needs a GPU, a llama-server build and the model weights in
/// the local cache. Run it on a developer machine:
/// <c>dotnet test --project tests/IronProw.LMSupply.Tests --filter "Category=RequiresGpu"</c>.
/// Temperature 0 keeps the comparison between the two configurations deterministic.
/// </para>
/// </remarks>
[Trait("Category", "RequiresGpu")]
public sealed class LocalSafeClientRealModelTests : IAsyncLifetime
{
    // A thinking-by-default model: the class of model the empty-answer failure needs.
    private const string Model = "gguf:gemma4-fast";

    // Small enough that reasoning would consume it whole, large enough for a short answer.
    private const int TightBudget = 64;

    private IGeneratorModel _generator = null!;

    public async ValueTask InitializeAsync()
        => _generator = await LocalGenerator.LoadAsync(Model, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _generator.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private IChatClient LocalSafe(LocalSafetyOptions options)
        => LMSupplyExtensions.BuildLocalSafeClient(
            _generator,
            new LazyReadinessProbe(() => true, [_generator.ModelId]),
            options);

    private static ChatMessage[] LongAnswerRequest() =>
    [
        new(ChatRole.User, "Write a 400-word essay about the sea."),
    ];

    private static ChatOptions Deterministic() => new() { Temperature = 0f };

    [Fact]
    public async Task A_length_bounded_call_returns_answer_text_rather_than_an_empty_string()
    {
        var client = LocalSafe(new LocalSafetyOptions { DefaultMaxOutputTokens = TightBudget });

        var response = await client.GetResponseAsync(
            LongAnswerRequest(), Deterministic(), TestContext.Current.CancellationToken);

        response.Text.Should().NotBeNullOrWhiteSpace(
            "the safety wrapper suppresses reasoning by default, so a tight budget buys answer text - " +
            "this returned an empty string with FinishReason.Length before 0.4.0");
    }

    [Fact]
    public async Task Without_the_reasoning_default_the_same_budget_is_spent_before_any_answer()
    {
        // The control for the fact above: the same model, the same budget, the wrapper's reasoning default
        // switched off (null = leave the model's own default alone). If this produced a normal answer too,
        // the fact above would be proving nothing about the fix.
        var client = LocalSafe(new LocalSafetyOptions
        {
            DefaultMaxOutputTokens = TightBudget,
            DefaultReasoningEffort = null,
        });

        var response = await client.GetResponseAsync(
            LongAnswerRequest(), Deterministic(), TestContext.Current.CancellationToken);

        var reasoning = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextReasoningContent>()
            .Any();

        (string.IsNullOrWhiteSpace(response.Text) || reasoning).Should().BeTrue(
            "with reasoning left on, a budget this small is spent thinking - the answer is empty, or the " +
            "reasoning that consumed it is carried back so the consumer can see why");
    }

    [Fact]
    public async Task The_response_carries_the_model_id_the_usage_and_the_provider_metadata()
    {
        var client = LocalSafe(new LocalSafetyOptions { DefaultMaxOutputTokens = TightBudget });

        var response = await client.GetResponseAsync(
            LongAnswerRequest(), Deterministic(), TestContext.Current.CancellationToken);

        response.ModelId.Should().Be(_generator.ModelId, "the bridge reports the model that answered");
        response.Usage.Should().NotBeNull("llama-server reports token usage and the bridge carries it");
        response.Usage!.OutputTokenCount.Should().BeGreaterThan(0);
        client.GetService<ChatClientMetadata>()?.ProviderName.Should().Be("LMSupply");
    }
}
