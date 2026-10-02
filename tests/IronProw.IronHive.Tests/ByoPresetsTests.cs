using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using IronHive.Providers.OpenAI.Compatible;
using IronHive.Providers.OpenAI.Compatible.GpuStack;
using IronProw.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronProw.IronHive.Tests;

public class ByoPresetsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_local_defaults_are_the_providers_own_defaults()
    {
        // The catalogue states the addresses an app shows; the providers decide where an empty base URL really goes.
        // These pin the two together so a provider default that moves cannot leave the catalogue showing the old one.
        new OpenAICompatibleConfig().ToOpenAI().BaseUrl.Should().Be(ByoPresets.Ollama.DefaultBaseUrl + "/v1");
        new GpuStackConfig().ToOpenAICompatible().ToOpenAI().BaseUrl.Should().StartWith(ByoPresets.GpuStack.DefaultBaseUrl + "/");
    }

    [Fact]
    public void Every_preset_has_a_unique_id_and_only_custom_requires_a_base_url()
    {
        ByoPresets.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        ByoPresets.All.Where(p => p.BaseUrlRequired).Select(p => p.Id).Should().Equal("custom");
        ByoPresets.All.Where(p => p.Kind == ProviderKind.Frontier).Select(p => p.Id)
            .Should().BeEquivalentTo("openai", "anthropic", "gemini", "grok");
        ByoPresets.All.Where(p => p.Kind == ProviderKind.Frontier).Should().OnlyContain(p => p.ApiKeyRequired);
        ByoPresets.Find(" OpenAI ").Should().BeSameAs(ByoPresets.OpenAI);
    }

    [Theory]
    [InlineData("ollama", null, null, null)]
    [InlineData("ollama", "http://192.168.0.5:11434", null, null)]
    [InlineData("custom", null, null, "needs a base URL")]
    [InlineData("custom", "localhost:1234", null, "not an absolute http or https URL")]
    [InlineData("custom", "ftp://host", null, "not an absolute http or https URL")]
    [InlineData("openai", null, null, "needs an API key")]
    [InlineData("openai", null, "sk-x", null)]
    [InlineData("bedrock", null, "k", "Unknown preset 'bedrock'")]
    public void Validate_states_why_an_endpoint_cannot_be_used(string preset, string? baseUrl, string? key, string? expected)
    {
        var error = ByoPresets.Validate(new ByoEndpoint(preset, baseUrl, key));

        if (expected is null)
        {
            error.Should().BeNull();
        }
        else
        {
            error.Should().Contain(expected);
        }
    }

    [Fact]
    public void Each_preset_registers_a_candidate_of_its_kind()
    {
        var services = new ServiceCollection();
        var builder = services.AddIronProw();
        builder.AddIronHiveByo("a", 1, "m", new ByoEndpoint("openai", ApiKey: "sk"));
        builder.AddIronHiveByo("b", 1, "m", new ByoEndpoint("grok", ApiKey: "xai"));
        builder.AddIronHiveByo("c", 1, "m", new ByoEndpoint("ollama"));
        builder.AddIronHiveByo("d", 1, "m", new ByoEndpoint("custom", "http://host:1234"));

        var registered = services.BuildServiceProvider().GetRequiredService<IProviderRegistry>().GetOrdered();

        registered.Select(r => (r.Id, r.Kind)).Should().BeEquivalentTo(
            [("a", ProviderKind.Frontier), ("b", ProviderKind.Frontier), ("c", ProviderKind.Lan), ("d", ProviderKind.Lan)]);
    }

    [Fact]
    public void Registering_an_invalid_endpoint_throws_with_the_reason()
    {
        var builder = new ServiceCollection().AddIronProw();

        var act = () => builder.AddIronHiveByo("x", 1, "m", new ByoEndpoint("custom"));

        act.Should().Throw<ArgumentException>().WithMessage("*needs a base URL*");
    }

    [Fact]
    public async Task Probe_lists_the_models_of_a_reachable_server_with_the_key()
    {
        using var server = StubServer.Start(request =>
            request.Headers["Authorization"] == "Bearer good"
                ? (200, """{"object":"list","data":[{"id":"m1","object":"model","created":0,"owned_by":"x"},{"id":"m2","object":"model","created":0,"owned_by":"x"}]}""")
                : (401, """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error"}}"""));

        var ok = await ByoPresets.ProbeAsync(new ByoEndpoint("custom", server.BaseUrl, "good"), cancellationToken: Ct);
        var refused = await ByoPresets.ProbeAsync(new ByoEndpoint("custom", server.BaseUrl, "bad"), cancellationToken: Ct);

        ok.Ok.Should().BeTrue();
        ok.ModelIds.Should().Equal("m1", "m2");
        ok.ModelCount.Should().Be(2);
        ok.Lists("m2").Should().BeTrue();
        ok.Lists("m3").Should().BeFalse("a listed id is matched exactly");
        (ok.StatusCode, ok.Error).Should().Be(((int?)null, (string?)null));
        server.Paths.Should().AllBe("/v1/models");
        refused.Ok.Should().BeFalse();
        refused.StatusCode.Should().Be(401);
        refused.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Probe_of_an_address_nobody_listens_on_fails_instead_of_reporting_connected()
    {
        var port = FreePort();

        var result = await ByoPresets.ProbeAsync(new ByoEndpoint("ollama", $"http://127.0.0.1:{port}"), cancellationToken: Ct);

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Probe_of_an_invalid_endpoint_does_not_contact_anything()
    {
        var result = await ByoPresets.ProbeAsync(new ByoEndpoint("anthropic"), cancellationToken: Ct);

        result.Ok.Should().BeFalse();
        result.ModelIds.Should().BeEmpty();
        result.Error.Should().Be("The 'anthropic' preset needs an API key.");
    }

    [Fact]
    public async Task Probe_sends_one_request_to_a_failing_server_and_reports_its_status()
    {
        // The SDK would resend a 503 three times with backoff; a connection check answers once.
        using var server = StubServer.Start(_ => (503, """{"error":{"message":"overloaded"}}"""));

        var result = await ByoPresets.ProbeAsync(new ByoEndpoint("custom", server.BaseUrl), cancellationToken: Ct);

        server.Paths.Should().ContainSingle();
        result.Ok.Should().BeFalse();
        result.StatusCode.Should().Be(503);
        result.Error.Should().Contain("overloaded");
    }

    [Fact]
    public async Task Probe_of_a_refused_connection_names_the_cause_promptly()
    {
        var port = FreePort();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = await ByoPresets.ProbeAsync(new ByoEndpoint("ollama", $"http://127.0.0.1:{port}"), cancellationToken: Ct);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "one attempt, not four with backoff");
        result.Error.Should().NotContain("Retry failed").And.NotContain("tries");
        result.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task Probe_of_a_server_that_never_answers_reports_the_timeout()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;

        var result = await ByoPresets.ProbeAsync(
            new ByoEndpoint("ollama", $"http://127.0.0.1:{port}"), TimeSpan.FromMilliseconds(500), Ct);

        result.Ok.Should().BeFalse();
        result.Error.Should().Be("No answer within 0.5 s.");
    }

    [Fact]
    public async Task The_endpoints_headers_reach_the_server_on_the_probe_and_on_chat()
    {
        const string Completion = """
            {"id":"c1","object":"chat.completion","created":0,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
            """;
        var seen = new List<string?>();
        using var server = StubServer.Start(request =>
        {
            lock (seen)
            {
                seen.Add(request.Headers["X-Gateway-Token"]);
            }

            return request.Url!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
                ? (200, """{"object":"list","data":[{"id":"m","object":"model","created":0,"owned_by":"x"}]}""")
                : (200, Completion);
        });
        var endpoint = new ByoEndpoint("custom", server.BaseUrl, Headers: new Dictionary<string, string> { ["X-Gateway-Token"] = "gw-1" });

        var probe = await ByoPresets.ProbeAsync(endpoint, cancellationToken: Ct);

        var services = new ServiceCollection();
        services.AddIronProw().AddIronHiveByo("gw", 1, "m", endpoint);
        await using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IProviderRegistry>().GetOrdered().Single();
        var reply = await registration.ClientFactory(provider).GetResponseAsync("hello", cancellationToken: Ct);

        probe.Lists("m").Should().BeTrue();
        reply.Text.Should().Be("hi");
        server.Paths.Should().Equal("/v1/models", "/v1/chat/completions");
        seen.Should().Equal("gw-1", "gw-1");
    }

    [Theory]
    [InlineData("custom", "Authorization")]
    [InlineData("anthropic", "x-api-key")]
    public void A_header_that_carries_the_credential_is_refused_without_contacting_anything(string preset, string header)
    {
        var endpoint = new ByoEndpoint(preset, preset == "custom" ? "http://127.0.0.1:9" : null, "k",
            new Dictionary<string, string> { [header] = "secret" });

        var error = ByoPresets.Validate(endpoint);
        var register = () => new ServiceCollection().AddIronProw().AddIronHiveByo("x", 1, "m", endpoint);

        error.Should().Contain(header);
        register.Should().Throw<ArgumentException>().WithMessage($"*{header}*");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StubServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<HttpListenerRequest, (int Status, string Body)> _respond;

        private StubServer(Func<HttpListenerRequest, (int, string)> respond, int port)
        {
            _respond = respond;
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
        }

        public string BaseUrl { get; }

        public List<string> Paths { get; } = [];

        public static StubServer Start(Func<HttpListenerRequest, (int, string)> respond)
        {
            var server = new StubServer(respond, FreePort());
            server._listener.Start();
            _ = server.LoopAsync();
            return server;
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                lock (Paths)
                {
                    Paths.Add(context.Request.Url!.AbsolutePath);
                }

                var (status, body) = _respond(context.Request);
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }
}
