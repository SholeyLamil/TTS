using InfluxDB.Client.Writes;

namespace Tts.Api.Services;

/// <summary>
/// Writes a single point to InfluxDB. Decouples the worker from the InfluxDB client so the
/// storage backend can be substituted or mocked in tests.
/// </summary>
public interface IInfluxWriter
{
    Task WriteAsync(PointData point, CancellationToken cancellationToken);
}
