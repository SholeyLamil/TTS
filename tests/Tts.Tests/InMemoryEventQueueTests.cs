using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Models;
using Tts.Api.Queue;
using Xunit;

namespace Tts.Tests;

public class InMemoryEventQueueTests
{
    private static InMemoryEventQueue QueueWithSize(int size)
    {
        var options = Options.Create(new TelemetryOptions { QueueSize = size });
        return new InMemoryEventQueue(options, NullLogger<InMemoryEventQueue>.Instance);
    }

    private static TransactionEventDto Event(string id) => new()
    {
        EventType = "TRANSACTION_CREATED",
        TransactionId = id,
        EventTimestamp = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Enqueued_events_are_dequeued_in_order()
    {
        var queue = QueueWithSize(10);
        await queue.EnqueueAsync(Event("a"));
        await queue.EnqueueAsync(Event("b"));

        var drained = new List<string>();
        await foreach (var e in queue.DequeueAsync(CancellationToken.None))
        {
            drained.Add(e.TransactionId);
            if (drained.Count == 2) break;
        }

        Assert.Equal(new[] { "a", "b" }, drained);
    }

    [Fact]
    public async Task When_full_the_newest_event_is_dropped()
    {
        var queue = QueueWithSize(2);
        await queue.EnqueueAsync(Event("a"));
        await queue.EnqueueAsync(Event("b"));
        await queue.EnqueueAsync(Event("c")); // queue full -> dropped

        var drained = new List<string>();
        await foreach (var e in queue.DequeueAsync(CancellationToken.None))
        {
            drained.Add(e.TransactionId);
            if (drained.Count == 2) break;
        }

        Assert.Equal(new[] { "a", "b" }, drained);
    }
}
