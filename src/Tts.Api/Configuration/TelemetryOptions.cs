namespace Tts.Api.Configuration;

/// <summary>
/// Service-level configuration, bound from the <c>Telemetry</c> section of
/// configuration (appsettings.json) and overridable by environment variables
/// (e.g. <c>Telemetry__RetryCount</c>).
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>How many times the InfluxDB client retries a failed batch write before giving up.</summary>
    public int RetryCount { get; set; } = 5;
}
