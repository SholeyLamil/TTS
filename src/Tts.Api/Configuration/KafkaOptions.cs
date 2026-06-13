namespace Tts.Api.Configuration;

/// <summary>
/// Kafka consumer settings, bound from the <c>Kafka</c> configuration section and
/// overridable by environment variables (e.g. <c>Kafka__BootstrapServers</c>).
/// </summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>Comma-separated broker addresses, e.g. <c>kafka:9092</c>.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Topic carrying transaction telemetry events.</summary>
    public string Topic { get; set; } = "transaction-events";

    /// <summary>Consumer group id. All instances sharing this id split the partitions.</summary>
    public string GroupId { get; set; } = "tts-influx-writer";

    /// <summary>Where to start when the group has no committed offset: <c>Earliest</c> or <c>Latest</c>.</summary>
    public string AutoOffsetReset { get; set; } = "Earliest";
}
