using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace IronProw.Core.Tests;

/// <summary>
/// The detection rule a consumer shipped for its local and remote generators (window 240, unit ≤ 60, 4 back-to-back
/// repeats, a unit must contain a letter), moved into the gateway with that consumer's cases.
/// </summary>
public class DegenerationStopTests
{
    [Theory]
    [InlineData("Here is the summary: concisely concisely concisely concisely")]
    [InlineData("So: the answer is that the answer is that the answer is that the answer is that ")]
    [InlineData("and the and the and the and the and the")]
    [InlineData("요약하면 결론은 결론은 결론은 결론은 ")]
    [InlineData("to to to to to")]
    public void Repetition_at_the_end_is_degeneration(string text)
        => DegenerationDetector.FindRepeatingUnit(text).Should().NotBeNull();

    [Theory]
    [InlineData("Local models can loop on a word; this sentence is ordinary prose with no repeated tail at all.")]
    [InlineData("| Name | Value |\n| --- | --- |\n| --- | --- |\n| --- | --- |\n| --- | --- |")]
    [InlineData("- one\n- two\n- three\n- four\n- five\n")]
    [InlineData("--------------------")]
    [InlineData("====================")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!")]
    [InlineData("....................")]
    [InlineData("")]
    [InlineData("hi")]
    public void Prose_and_markdown_structure_are_not_degeneration(string text)
        => DegenerationDetector.FindRepeatingUnit(text).Should().BeNull();

    [Fact]
    public void Only_the_window_is_read()
    {
        var text = string.Concat(Enumerable.Repeat("loop ", 10)) + new string('x', 0) +
                   " and then the model recovered and wrote a long ordinary ending that has no repetition in it whatsoever, " +
                   "going on for well beyond the inspected window so the early loop is out of sight entirely by now.";
        DegenerationDetector.FindRepeatingUnit(text).Should().BeNull();
    }

    [Fact]
    public async Task Streaming_stops_at_the_loop_and_says_why()
    {
        var inner = new ScriptedClient(["The result is ", "concisely ", "concisely ", "concisely ", "concisely ", "concisely ", "never reached"]);
        var sut = inner.WithDegenerationStop();

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(u);

        updates[^1].FinishReason.Should().Be(DegenerationStopChatClient.FinishReason);
        updates[^1].AdditionalProperties![DegenerationStopChatClient.RepeatedUnitKey].Should().Be("concisely ");
        string.Concat(updates.Select(u => u.Text)).Should().NotContain("never reached");
        inner.Pulled.Should().Be(5, "the inner stream is abandoned at the fourth repeat");
        inner.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Clean_output_passes_through_untouched()
    {
        var chunks = new[] { "Paris is the capital ", "of France. ", "| a | b |\n| --- | --- |\n| --- | --- |\n| --- | --- |\n| --- | --- |" };
        var sut = new ScriptedClient(chunks).WithDegenerationStop();

        var updates = new List<ChatResponseUpdate>();
        await foreach (var u in sut.GetStreamingResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(u);

        updates.Select(u => u.Text).Should().Equal(chunks);
        updates.Should().NotContain(u => u.FinishReason == DegenerationStopChatClient.FinishReason);
    }

    [Fact]
    public async Task Non_streaming_call_stops_early_and_reports_the_finish_reason()
    {
        var inner = new ScriptedClient(["word ", "word ", "word ", "word ", "word ", "word ", "word ", "word "]);
        var sut = inner.WithDegenerationStop();

        var response = await sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        response.FinishReason.Should().Be(DegenerationStopChatClient.FinishReason);
        response.Text.Should().Be("word word word word ");
        inner.Pulled.Should().Be(4);
    }

    private sealed class ScriptedClient(string[] chunks) : IChatClient
    {
        public int Pulled { get; private set; }
        public bool Disposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the stop client must serve non-streaming calls through the stream");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (var chunk in chunks)
                {
                    await Task.Yield();
                    Pulled++;
                    yield return new ChatResponseUpdate(ChatRole.Assistant, chunk) { ResponseId = "r1", MessageId = "m1" };
                }
            }
            finally
            {
                Disposed = true;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
