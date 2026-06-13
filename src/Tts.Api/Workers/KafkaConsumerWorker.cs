using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Models;
using Tts.Api.Services;

namespace Tts.Api.Workers;

/// <summary>
/// Subscribes to the Kafka topic and, for every message received, transforms the event and
/// writes it to InfluxDB. Kafka is the durable buffer (replacing the old in-memory queue):
/// offsets are only stored after a message has been handed to the writer, so a crash re-delivers
/// rather than loses (at-least-once). Malformed or invalid messages are logged and skipped so one
/// bad message can never stall the stream.
/// </summary>
public sealed class KafkaConsumerWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly KafkaOptions _kafka;
    private readonly IEventTransformer _transformer;
    private readonly IInfluxWriter _writer;
    private readonly ILogger<KafkaConsumerWorker> _logger;

    public KafkaConsumerWorker(
        IOptions<KafkaOptions> kafka,
        IEventTransformer transformer,
        IInfluxWriter writer,
        ILogger<KafkaConsumerWorker> logger)
    {
        _kafka = kafka.Value;
        _transformer = transformer;
        _writer = writer;
        _logger = logger;
    }

    // The Kafka consume loop is blocking, so run it on a background thread to free the host startup path.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = _kafka.GroupId,
            AutoOffsetReset = Enum.TryParse<AutoOffsetReset>(_kafka.AutoOffsetReset, ignoreCase: true, out var reset)
                ? reset : AutoOffsetReset.Earliest,
            EnableAutoCommit = true,        // commit stored offsets periodically in the background
            EnableAutoOffsetStore = false,  // ...but only store an offset once we've processed the message
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason} (fatal={Fatal})", e.Reason, e.IsFatal))
            .Build();

        consumer.Subscribe(_kafka.Topic);
        _logger.LogInformation("Subscribed to Kafka topic '{Topic}' (group '{Group}') at {Servers}",
            _kafka.Topic, _kafka.GroupId, _kafka.BootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<Ignore, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                {
                    // Topic not created yet — it appears once a producer first publishes.
                    // Benign: wait briefly instead of spamming error logs.
                    _logger.LogDebug("Topic '{Topic}' not available yet; waiting", _kafka.Topic);
                    Thread.Sleep(2000);
                    continue;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
                    continue;
                }

                if (result?.Message?.Value is { } value)
                {
                    ProcessMessage(value, stoppingToken);
                    consumer.StoreOffset(result); // mark processed; auto-commit will persist it
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            consumer.Close(); // leave the group cleanly and commit final offsets
            _logger.LogInformation("Kafka consumer stopped");
        }
    }

    private void ProcessMessage(string value, CancellationToken cancellationToken)
    {
        TransactionEventDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<TransactionEventDto>(value, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Skipping malformed Kafka message");
            return;
        }

        if (dto is null
            || string.IsNullOrWhiteSpace(dto.EventType)
            || string.IsNullOrWhiteSpace(dto.TransactionId)
            || dto.EventTimestamp is null)
        {
            _logger.LogWarning("Skipping Kafka message missing required fields (eventType/transactionId/eventTimestamp)");
            return;
        }

        dto.Data ??= new();

        try
        {
            var point = _transformer.Transform(dto);
            _writer.WriteAsync(point, cancellationToken).GetAwaiter().GetResult();
            _logger.LogDebug("Wrote event {EventType} for transaction {TransactionId}", dto.EventType, dto.TransactionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write event {EventType} for transaction {TransactionId}", dto.EventType, dto.TransactionId);
        }
    }
}
