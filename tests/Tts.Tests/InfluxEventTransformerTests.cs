using System.Text.Json;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Models;
using Tts.Api.Services;
using Xunit;

namespace Tts.Tests;

public class InfluxEventTransformerTests
{
    private static InfluxEventTransformer Transformer() =>
        new(Options.Create(new InfluxDbOptions { Measurement = "transaction_events" }));

    // Mirror how System.Text.Json materialises the Data bag (values arrive as JsonElement).
    private static Dictionary<string, object?> Data(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

    [Fact]
    public void Maps_measurement_tags_and_count_field()
    {
        var line = Transformer().Transform(new TransactionEventDto
        {
            EventType = "TRANSACTION_CREATED",
            TransactionId = "TXN123",
            EventTimestamp = DateTimeOffset.UnixEpoch,
        }).ToLineProtocol();

        Assert.StartsWith("transaction_events,", line);
        Assert.Contains("eventType=TRANSACTION_CREATED", line);
        Assert.Contains("transactionId=TXN123", line);
        Assert.Contains("count=1i", line);
    }

    [Fact]
    public void Maps_data_bag_to_typed_fields()
    {
        var line = Transformer().Transform(new TransactionEventDto
        {
            EventType = "TRANSACTION_COMPLETED",
            TransactionId = "TXN123",
            EventTimestamp = DateTimeOffset.UnixEpoch,
            Data = Data("""{ "amount": 99.5, "retries": 2, "currency": "USD", "success": true }"""),
        }).ToLineProtocol();

        Assert.Contains("amount=99.5", line);   // double field
        Assert.Contains("retries=2i", line);     // integer field
        Assert.Contains("currency=\"USD\"", line); // string field
        Assert.Contains("success=true", line);   // boolean field
    }

    [Fact]
    public void Empty_data_still_produces_a_writable_point()
    {
        var line = Transformer().Transform(new TransactionEventDto
        {
            EventType = "TRANSACTION_FAILED",
            TransactionId = "TXN999",
            EventTimestamp = DateTimeOffset.UnixEpoch,
        }).ToLineProtocol();

        // InfluxDB rejects fieldless points; the count marker guarantees one field.
        Assert.Contains("count=1i", line);
    }
}
