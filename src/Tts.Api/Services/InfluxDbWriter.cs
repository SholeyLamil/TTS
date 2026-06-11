using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;

namespace Tts.Api.Services;

/// <summary>
/// Writes points to InfluxDB using the client's buffered batch API: points are queued and
/// flushed in bulk (every <c>BatchSize</c> points or <c>FlushInterval</c> ms), turning one
/// network round-trip per point into one per batch — a large throughput gain.
/// The client retries transient failures internally; errors are surfaced via the event handler
/// and logged, never re-thrown, so a database outage cannot break the pipeline.
/// </summary>
public sealed class InfluxDbWriter : IInfluxWriter, IDisposable
{
    private readonly InfluxDbOptions _influx;
    private readonly WriteApi _writeApi;
    private readonly ILogger<InfluxDbWriter> _logger;

    public InfluxDbWriter(
        IInfluxDBClient client,
        IOptions<InfluxDbOptions> influx,
        IOptions<TelemetryOptions> telemetry,
        ILogger<InfluxDbWriter> logger)
    {
        _influx = influx.Value;
        _logger = logger;

        var writeOptions = new WriteOptions
        {
            BatchSize = 5_000,                                 // flush once 5k points are buffered
            FlushInterval = 1_000,                             // ...or at least every second
            RetryInterval = 1_000,
            MaxRetries = Math.Max(1, telemetry.Value.RetryCount),
        };

        // Cast to the concrete type to access the error EventHandler (not on IWriteApi).
        _writeApi = (WriteApi)client.GetWriteApi(writeOptions);

        // Batch writes happen in the background, so failures arrive as events rather than exceptions.
        _writeApi.EventHandler += (_, args) =>
        {
            switch (args)
            {
                case WriteErrorEvent e:
                    _logger.LogError(e.Exception, "InfluxDB batch write failed");
                    break;
                case WriteRetriableErrorEvent e:
                    _logger.LogWarning(e.Exception, "InfluxDB write error; client retrying");
                    break;
                case WriteRuntimeExceptionEvent e:
                    _logger.LogError(e.Exception, "InfluxDB write runtime exception");
                    break;
            }
        };
    }

    public Task WriteAsync(PointData point, CancellationToken cancellationToken)
    {
        // Non-blocking: enqueues into the client's batch buffer and returns immediately.
        _writeApi.WritePoint(point, _influx.Bucket, _influx.Organisation);
        return Task.CompletedTask;
    }

    // Flushes any buffered points on shutdown.
    public void Dispose() => _writeApi.Dispose();
}
