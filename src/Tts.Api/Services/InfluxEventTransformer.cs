using System.Text.Json;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Models;

namespace Tts.Api.Services;

/// <summary>
/// Maps a <see cref="TransactionEventDto"/> onto one InfluxDB point:
/// <list type="bullet">
///   <item>measurement = configured measurement name</item>
///   <item>tags = <c>eventType</c>, <c>transactionId</c></item>
///   <item>fields = a <c>count</c> marker plus every entry in <see cref="TransactionEventDto.Data"/></item>
///   <item>timestamp = the event timestamp</item>
/// </list>
/// </summary>
public sealed class InfluxEventTransformer : IEventTransformer
{
    private readonly string _measurement;

    public InfluxEventTransformer(IOptions<InfluxDbOptions> options)
    {
        _measurement = options.Value.Measurement;
    }

    public PointData Transform(TransactionEventDto dto)
    {
        var point = PointData.Measurement(_measurement)
            .Tag("eventType", dto.EventType)
            .Tag("transactionId", dto.TransactionId)
            // Always present so events remain countable even when Data is empty
            // (InfluxDB requires every point to carry at least one field).
            .Field("count", 1L)
            .Timestamp((dto.EventTimestamp ?? DateTimeOffset.UtcNow).UtcDateTime, WritePrecision.Ns);

        foreach (var (key, value) in dto.Data)
            point = AddField(point, key, value);

        return point;
    }

    private static PointData AddField(PointData point, string key, object? value) => value switch
    {
        null => point,
        JsonElement element => AddJsonField(point, key, element),
        bool b => point.Field(key, b),
        // All numeric data-bag values are stored as float for a consistent field type.
        // The same field name can carry 250 in one event and 99.5 in another; InfluxDB
        // locks a field to one type, so mixing int/float would make it reject points.
        sbyte or byte or short or ushort or int or uint or long
            or float or double or decimal => point.Field(key, Convert.ToDouble(value)),
        string s => point.Field(key, s),
        _ => point.Field(key, value.ToString() ?? string.Empty),
    };

    // System.Text.Json deserialises Dictionary<string, object?> values as JsonElement,
    // so map each JSON kind onto the right InfluxDB field type. Numbers are always written
    // as float to avoid int/float field-type conflicts across events (see AddField).
    private static PointData AddJsonField(PointData point, string key, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => point.Field(key, element.GetDouble()),
        JsonValueKind.True or JsonValueKind.False => point.Field(key, element.GetBoolean()),
        JsonValueKind.String => point.Field(key, element.GetString() ?? string.Empty),
        JsonValueKind.Null or JsonValueKind.Undefined => point,
        // Nested objects/arrays are stored as their raw JSON so nothing is silently lost.
        _ => point.Field(key, element.GetRawText()),
    };
}
