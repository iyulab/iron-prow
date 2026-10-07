#pragma warning disable CA2012
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace IronProw.Core.Tests;

/// <summary>
/// The response-caching recipe the README documents: Microsoft.Extensions.AI's distributed cache stage outside the
/// guarded client. A repeat request is answered from the cache (streaming replays as a stream); a different
/// cache key scope (another tenant) does not share entries.
/// </summary>
public class ResponseCachingRecipeTests
{
    [Fact]
    public async Task Repeat_request_is_served_from_the_cache_and_tenants_do_not_share_entries()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new CountingChatClient();
        IChatClient guarded = new GuardedChatClient(provider, AllowAll());
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        IChatClient tenantA = new ChatClientBuilder(guarded)
            .UseDistributedCache(cache, c => c.CacheKeyAdditionalValues = ["tenant-a"])
            .Build();
        IChatClient tenantB = new ChatClientBuilder(guarded)
            .UseDistributedCache(cache, c => c.CacheKeyAdditionalValues = ["tenant-b"])
            .Build();

        List<ChatMessage> request = [new(ChatRole.User, "What is the capital of France?")];

        (await tenantA.GetResponseAsync(request, cancellationToken: ct)).Text.Should().Be("answer 1");
        (await tenantA.GetResponseAsync(request, cancellationToken: ct)).Text.Should().Be("answer 1");
        provider.Calls.Should().Be(1);

        (await tenantB.GetResponseAsync(request, cancellationToken: ct)).Text.Should().Be("answer 2");
        provider.Calls.Should().Be(2);

        var first = await Collect(tenantA.GetStreamingResponseAsync(request, cancellationToken: ct));
        var replay = await Collect(tenantA.GetStreamingResponseAsync(request, cancellationToken: ct));
        replay.Should().Be(first);
        provider.StreamingCalls.Should().Be(1);
    }

    private static IGuard AllowAll()
    {
        var guard = Substitute.For<IGuard>();
        guard.InspectInputAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GuardVerdict>(GuardVerdict.Allow()));
        guard.InspectOutputAsync(Arg.Any<ChatResponse>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GuardVerdict>(GuardVerdict.Allow()));
        return guard;
    }

    private static async Task<string> Collect(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var update in updates)
        {
            text.Append(update.Text);
        }

        return text.ToString();
    }

    private sealed class CountingChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public int StreamingCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"answer {Calls}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "streamed ");
            yield return new ChatResponseUpdate(ChatRole.Assistant, $"answer {StreamingCalls}");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
