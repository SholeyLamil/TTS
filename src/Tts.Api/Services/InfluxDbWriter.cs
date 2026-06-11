using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;

namespace Tts.Api.Services;

/// <summary>
/// Writes points to InfluxDB via the official client, retrying transient failures with
/// exponential backoff up to <see cref="TelemetryOptions.RetryCount"/> attempts. Failures are
/// always logged and swallowed — never re-thrown — so a database outage cannot break the pipeline.
/// </summary>
public sealed class InfluxDbWriter : IInfluxWriter
{
    private readonly IInfluxDBClient _client;
    private readonly InfluxDbOptions _influx;
    private readonly int _retryCount;
    private readonly ILogger<InfluxDbWriter> _logger;

    public InfluxDbWriter(
        IInfluxDBClient client,
        IOptions<InfluxDbOptions> influx,
        IOptions<TelemetryOptions> telemetry,
        ILogger<InfluxDbWriter> logger)
    {
        _client = client;
        _influx = influx.Value;
        _retryCount = Math.Max(1, telemetry.Value.RetryCount);
        _logger = logger;
    }

    public async Task WriteAsync(PointData point, CancellationToken cancellationToken)
    {
        var writeApi = _client.GetWriteApiAsync();

        for (var attempt = 1; attempt <= _retryCount; attempt++)
        {
            try
            {
                await writeApi.WritePointAsync(point, _influx.Bucket, _influx.Organisation, cancellationToken);
                _logger.LogDebug("Wrote point to InfluxDB on attempt {Attempt}/{Max}", attempt, _retryCount);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // shutdown — let the worker stop cleanly
            }
            catch (Exception ex) when (attempt < _retryCount)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(ex,
                    "InfluxDB write failed (attempt {Attempt}/{Max}); retrying in {Delay}",
                    attempt, _retryCount, delay);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "InfluxDB write failed permanently after {Max} attempts; event dropped", _retryCount);
                return;
            }
        }
    }
}
