using System.ComponentModel.DataAnnotations;

namespace Tts.Api.Models;

/// <summary>
/// The generic transaction telemetry event submitted by the payment gateway.
/// The schema is intentionally open: <see cref="Data"/> carries arbitrary transaction
/// attributes that may be defined later, so new event types need no model change.
/// </summary>
public sealed class TransactionEventDto
{
    /// <summary>The kind of event, e.g. <c>TRANSACTION_CREATED</c> (stored as an InfluxDB tag).</summary>
    [Required(AllowEmptyStrings = false)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>When the event occurred (ISO-8601). Required so points land at the right time.</summary>
    [Required]
    public DateTimeOffset? EventTimestamp { get; set; }

    /// <summary>Identifier correlating events belonging to the same transaction (InfluxDB tag).</summary>
    [Required(AllowEmptyStrings = false)]
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>Free-form transaction attributes. Each entry becomes an InfluxDB field.</summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}
