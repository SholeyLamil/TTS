namespace Tts.Api.Configuration;

/// <summary>
/// InfluxDB connection settings, bound from the <c>InfluxDb</c> configuration section
/// and overridable by environment variables (e.g. <c>InfluxDb__Token</c>).
/// </summary>
public sealed class InfluxDbOptions
{
    public const string SectionName = "InfluxDb";

    public string Url { get; set; } = "http://localhost:8086";
    public string Token { get; set; } = string.Empty;
    public string Organisation { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;

    /// <summary>InfluxDB measurement name all telemetry events are written under.</summary>
    public string Measurement { get; set; } = "transaction_events";
}
