using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Models;

namespace Tts.Api.Queue;

/// <summary>
/// In-memory <see cref="Channel{T}"/>-backed queue. Bounded by <see cref="TelemetryOptions.QueueSize"/>;
/// when full, the incoming event is dropped and logged rather than blocking the caller, so a slow
/// or unreachable InfluxDB can never slow down event ingestion.
/// </summary>
public sealed class InMemoryEventQueue : IEventQueue
{
    private readonly Channel<TransactionEventDto> _channel;
    private readonly ILogger<InMemoryEventQueue> _logger;

    public InMemoryEventQueue(IOptions<TelemetryOptions> options, ILogger<InMemoryEventQueue> logger)
    {
        _logger = logger;
        var capacity = Math.Max(1, options.Value.QueueSize);

        _channel = Channel.CreateBounded<TransactionEventDto>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            },
            itemDropped: dropped => _logger.LogWarning(
                "Event queue full (capacity {Capacity}); dropped event {EventType}/{TransactionId}",
                capacity, dropped.EventType, dropped.TransactionId));
    }

    public ValueTask EnqueueAsync(TransactionEventDto evt, CancellationToken cancellationToken = default)
    {
        // TryWrite never blocks; with DropWrite the itemDropped callback above handles a full queue.
        _channel.Writer.TryWrite(evt);
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<TransactionEventDto> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
