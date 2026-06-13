using System.Text.Json;
using Tts.Api.Models;
using Xunit;

namespace Tts.Tests;

// Covers the JSON-parsing assumption the Kafka consumer relies on.
public class TransactionEventDtoTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserializes_kafka_event_payload()
    {
        var json = """
        {"eventType":"TRANSACTION_COMPLETED","eventTimestamp":"2026-06-13T10:30:00Z","transactionId":"TXN123","data":{"amount":99.5,"currency":"USD"}}
        """;

        var dto = JsonSerializer.Deserialize<TransactionEventDto>(json, Options);

        Assert.NotNull(dto);
        Assert.Equal("TRANSACTION_COMPLETED", dto!.EventType);
        Assert.Equal("TXN123", dto.TransactionId);
        Assert.NotNull(dto.EventTimestamp);
        Assert.True(dto.Data.ContainsKey("amount"));
    }

    [Fact]
    public void Matches_properties_case_insensitively()
    {
        // Producers may use PascalCase keys; the consumer parses case-insensitively.
        var json = """{"EventType":"TRANSACTION_FAILED","EventTimestamp":"2026-06-13T10:30:00Z","TransactionId":"T1","Data":{}}""";

        var dto = JsonSerializer.Deserialize<TransactionEventDto>(json, Options);

        Assert.Equal("TRANSACTION_FAILED", dto!.EventType);
        Assert.Equal("T1", dto.TransactionId);
    }

    [Fact]
    public void Missing_data_yields_empty_bag_not_null()
    {
        var json = """{"eventType":"X","eventTimestamp":"2026-06-13T10:30:00Z","transactionId":"T1"}""";

        var dto = JsonSerializer.Deserialize<TransactionEventDto>(json, Options);

        Assert.NotNull(dto!.Data);
        Assert.Empty(dto.Data);
    }
}
