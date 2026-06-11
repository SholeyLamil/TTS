using Tts.Api.Models;

namespace Tts.Api.Queue;

/// <summary>
/// Contract for the hand-off buffer between the API (producers) and the background
/// worker (consumer). Keeping this an interface lets the in-memory <see cref="Channel{T}"/>
/// implementation be swapped for Kafka, RabbitMQ, Redis, or Azure Service Bus later
/// without touching the API or worker.
/// </summary>
public interface IEventQueue
{
    /// <summary>Buffers an event. Returns near-instantly and never blocks request handling.</summary>
    ValueTask EnqueueAsync(TransactionEventDto evt, CancellationToken cancellationToken = default);

    /// <summary>Yields buffered events as they arrive. Consumed only by the background worker.</summary>
    IAsyncEnumerable<TransactionEventDto> DequeueAsync(CancellationToken cancellationToken);
}
