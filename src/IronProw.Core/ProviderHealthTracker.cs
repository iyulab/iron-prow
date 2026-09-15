using System.Collections.Concurrent;

namespace IronProw.Core;

/// <summary>
/// Remembers, per provider id, how many consecutive calls failed and until when the provider is
/// cooling down. The gateway demotes a cooling provider behind the healthy ones instead of paying its
/// retries and fallback on every call — a dead LAN provider otherwise costs every request the full
/// retry budget before the gateway degrades past it.
/// </summary>
/// <remarks>
/// A cooling provider is demoted, never excluded: when no healthy provider remains it is still tried.
/// A success clears the provider's record; a failure while cooling re-arms the cooldown from now.
/// Only failures the gateway degrades on count (retryable exhausted, fallback-eligible) — a terminal
/// failure such as a guard block or a caller cancellation says nothing about the provider's health.
/// </remarks>
internal sealed class ProviderHealthTracker(ResilienceOptions options, TimeProvider time)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public int ConsecutiveFailures;
        public DateTimeOffset CoolingUntil = DateTimeOffset.MinValue;
    }

    /// <summary>Whether health memory is on at all (<see cref="ResilienceOptions.FailureThreshold"/> &gt; 0).</summary>
    public bool Enabled => options.FailureThreshold > 0;

    /// <summary>Whether <paramref name="providerId"/> is cooling down right now.</summary>
    public bool IsCoolingDown(string providerId)
        => Enabled
           && _entries.TryGetValue(providerId, out var entry)
           && entry.CoolingUntil > time.GetUtcNow();

    /// <summary>A call to <paramref name="providerId"/> succeeded: clear its record.</summary>
    public void RecordSuccess(string providerId)
    {
        if (!Enabled) return;
        _entries.TryRemove(providerId, out _);
    }

    /// <summary>
    /// A call to <paramref name="providerId"/> failed in a way the gateway degrades on. Reaching
    /// <see cref="ResilienceOptions.FailureThreshold"/> consecutive failures starts (or re-arms) a
    /// cooldown of <see cref="ResilienceOptions.Cooldown"/>.
    /// </summary>
    /// <returns><see langword="true"/> when this failure started or re-armed a cooldown.</returns>
    public bool RecordFailure(string providerId)
    {
        if (!Enabled) return false;
        var entry = _entries.GetOrAdd(providerId, static _ => new Entry());
        lock (entry)
        {
            entry.ConsecutiveFailures++;
            if (entry.ConsecutiveFailures < options.FailureThreshold)
                return false;
            entry.CoolingUntil = time.GetUtcNow() + options.Cooldown;
            return true;
        }
    }
}
