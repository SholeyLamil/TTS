using Tts.Api.Models;
using Tts.Api.Queue;
using Tts.Api.Services;

namespace Tts.Api.Workers;

/// <summary>
/// Background service that drains <see cref="IEventQueue"/> and persists each event to InfluxDB.
/// Processes one event at a time; any failure is caught and logged so a single bad event can never
/// stall the pipeline or crash the host.
/// </summary>
public sealed class EventProcessingWorker : BackgroundService
{
    private readonly IEventQueue _queue;
    private readonly IEventTransformer _transformer;
    private readonly IInfluxWriter _writer;
    private readonly ILogger<EventProcessingWorker> _logger;

    public EventProcessingWorker(
        IEventQueue queue,
        IEventTransformer transformer,
        IInfluxWriter writer,
        ILogger<EventProcessingWorker> logger)
    {
        _queue = queue;
        _transformer = transformer;
        _writer = writer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event processing worker started");

        try
        {
            await foreach (var evt in _queue.DequeueAsync(stoppingToken))
                await ProcessEventAsync(evt, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("Event processing worker stopped");
    }

    private async Task ProcessEventAsync(TransactionEventDto evt, CancellationToken cancellationToken)
    {
        try
        {
            var point = _transformer.Transform(evt);
            await _writer.WriteAsync(point, cancellationToken);
            _logger.LogInformation(
                "Processed event {EventType} for transaction {TransactionId}", evt.EventType, evt.TransactionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process event {EventType} for transaction {TransactionId}", evt.EventType, evt.TransactionId);
        }
    }
}
