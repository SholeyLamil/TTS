namespace Tts.Api.Configuration;

/// <summary>
/// Service-level configuration, bound from the <c>Telemetry</c> section of
/// configuration (appsettings.json) and overridable by environment variables
/// (e.g. <c>Telemetry__ApiKey</c>). No secrets are hardcoded.
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Shared secret required in the <c>X-API-Key</c> header. Empty = reject every request.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maximum events buffered in memory; when full the newest event is dropped and logged.</summary>
    public int QueueSize { get; set; } = 10_000;

    /// <summary>How many times the writer attempts an InfluxDB write before giving up on an event.</summary>
    public int RetryCount { get; set; } = 5;
}
