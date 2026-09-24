using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

/// <summary>
/// HTTP failures are classified by status, whichever exception type carries it — the OpenAI SDK's
/// <see cref="ClientResultException"/> used to fall through to FallbackEligible for every status, so a 500 was never
/// retried and a <see cref="HttpRequestException"/> 401 was retried.
/// </summary>
public class HttpStatusClassificationTests
{
    private readonly DefaultErrorClassifier _sut = new();

    [Theory]
    [InlineData(408, ErrorClassification.Retryable)]
    [InlineData(500, ErrorClassification.Retryable)]
    [InlineData(502, ErrorClassification.Retryable)]
    [InlineData(504, ErrorClassification.Retryable)]
    [InlineData(429, ErrorClassification.FallbackEligible)] // no retry hint: a blind retry rarely outlasts a rate limit
    [InlineData(503, ErrorClassification.FallbackEligible)]
    [InlineData(400, ErrorClassification.FallbackEligible)]
    [InlineData(401, ErrorClassification.FallbackEligible)]
    [InlineData(404, ErrorClassification.FallbackEligible)]
    [InlineData(409, ErrorClassification.FallbackEligible)]
    [InlineData(501, ErrorClassification.FallbackEligible)]
    public void ClientResultException_is_classified_by_status(int status, ErrorClassification expected)
        => _sut.Classify(new ClientResultException(new FakeResponse(status))).Should().Be(expected);

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ErrorClassification.Retryable)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorClassification.FallbackEligible)] // was Retryable: every HttpRequestException was
    [InlineData(HttpStatusCode.TooManyRequests, ErrorClassification.FallbackEligible)]
    public void HttpRequestException_with_a_status_is_classified_by_it(HttpStatusCode status, ErrorClassification expected)
        => _sut.Classify(new HttpRequestException("failed", null, status)).Should().Be(expected);

    [Theory]
    [InlineData("retry-after-ms", "250")]
    [InlineData("Retry-After", "2")]
    public void Rate_limit_with_a_retry_hint_is_retryable(string header, string value)
    {
        var ex = new ClientResultException(new FakeResponse(429, (header, value)));
        _sut.Classify(ex).Should().Be(ErrorClassification.Retryable);
    }

    [Fact]
    public void Retry_hint_is_read_from_milliseconds_seconds_and_http_date()
    {
        var reader = new HttpStatusFailureReader();
        reader.Read(new ClientResultException(new FakeResponse(429, ("retry-after-ms", "250"))))!.Value.RetryAfter
            .Should().Be(TimeSpan.FromMilliseconds(250));
        reader.Read(new ClientResultException(new FakeResponse(503, ("Retry-After", "3"))))!.Value.RetryAfter
            .Should().Be(TimeSpan.FromSeconds(3));
        var date = DateTimeOffset.UtcNow.AddSeconds(30).ToString("R");
        reader.Read(new ClientResultException(new FakeResponse(429, ("Retry-After", date))))!.Value.RetryAfter
            .Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void A_transport_failure_without_a_status_keeps_the_type_rules()
    {
        _sut.Classify(new HttpRequestException("reset")).Should().Be(ErrorClassification.Retryable);
        _sut.Classify(new ClientResultException("no response")).Should().Be(ErrorClassification.FallbackEligible);
    }

    [Fact]
    public async Task Retry_waits_the_provider_hint_when_it_is_longer_than_the_backoff()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ClientResultException(new FakeResponse(429, ("retry-after-ms", "150"))),
                     _ => Task.FromResult(response));
        var sut = new ResilienceChatClient(inner, _sut, new ResilienceOptions { MaxRetries = 2, BaseDelay = TimeSpan.Zero });

        var watch = Stopwatch.StartNew();
        var result = await sut.GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(response);
        watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(140));
    }

    [Fact]
    public async Task A_hint_beyond_MaxRetryAfter_is_not_waited_on_that_provider()
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new ClientResultException(new FakeResponse(429, ("Retry-After", "60"))));
        var sut = new ResilienceChatClient(inner, _sut, new ResilienceOptions { MaxRetries = 2, BaseDelay = TimeSpan.Zero });

        var watch = Stopwatch.StartNew();
        await sut.Invoking(s => s.GetResponseAsync([new(ChatRole.User, "hi")]))
            .Should().ThrowAsync<ClientResultException>();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        await inner.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(500, 2, "first")]  // transient: retried on the same provider, which then answers
    [InlineData(409, 1, "second")] // provider-specific: no retry, the next provider answers
    [InlineData(429, 1, "second")] // rate limit with no hint: degrade at once rather than burn the retry budget
    public async Task Gateway_retries_or_degrades_by_status(int status, int firstProviderCalls, string answeredBy)
    {
        var calls = 0;
        var first = Substitute.For<IChatClient>();
        first.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? throw new ClientResultException(new FakeResponse(status))
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "first"))));
        var second = Substitute.For<IChatClient>();
        second.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "second"))));

        var services = new ServiceCollection();
        services.AddIronProw()
            .AddProvider("first", ProviderKind.Frontier, 10, _ => first)
            .AddProvider("second", ProviderKind.Frontier, 5, _ => second)
            .Configure(o =>
            {
                o.EnableFallback = true;
                o.Resilience.BaseDelay = TimeSpan.Zero;
            });
        await using var provider = services.BuildServiceProvider();

        var response = await provider.GetRequiredService<IChatClient>()
            .GetResponseAsync([new(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        response.Text.Should().Be(answeredBy);
        calls.Should().Be(firstProviderCalls);
    }

    [Fact]
    public void A_registered_reader_is_consulted_by_the_classifier_AddIronProw_registers()
    {
        var services = new ServiceCollection();
        services.AddIronProw();
        services.AddSingleton<IHttpFailureReader>(new MarkerReader());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IErrorClassifier>().Classify(new MarkerException())
            .Should().Be(ErrorClassification.Retryable);
    }

    private sealed class MarkerException : Exception;

    private sealed class MarkerReader : IHttpFailureReader
    {
        public HttpFailure? Read(Exception exception)
            => exception is MarkerException ? new HttpFailure(502, null) : null;
    }

    internal sealed class FakeResponse(int status, params (string Name, string Value)[] headers) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "fake";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.FromString("{}");
        protected override PipelineResponseHeaders HeadersCore { get; } = new FakeHeaders(headers);
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);
        public override void Dispose() { }
    }

    private sealed class FakeHeaders((string Name, string Value)[] headers) : PipelineResponseHeaders
    {
        public override bool TryGetValue(string name, out string? value)
        {
            foreach (var (n, v) in headers)
            {
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = v;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            var found = TryGetValue(name, out var value);
            values = found ? [value!] : null;
            return found;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)).GetEnumerator();
    }
}
