using InfluxDB.Client;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Services;
using Tts.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

// Strongly-typed configuration, bound from appsettings.json sections and overridable
// by environment variables (Kafka__BootstrapServers, InfluxDb__Token, ...).
builder.Services.Configure<TelemetryOptions>(builder.Configuration.GetSection(TelemetryOptions.SectionName));
builder.Services.Configure<InfluxDbOptions>(builder.Configuration.GetSection(InfluxDbOptions.SectionName));
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));

// One InfluxDB client for the whole app.
builder.Services.AddSingleton<IInfluxDBClient>(sp =>
{
    var influx = sp.GetRequiredService<IOptions<InfluxDbOptions>>().Value;
    return new InfluxDBClient(influx.Url, influx.Token);
});

// Processing pipeline: Kafka consumer -> transformer -> batched InfluxDB writer.
builder.Services.AddSingleton<IEventTransformer, InfluxEventTransformer>();
builder.Services.AddSingleton<IInfluxWriter, InfluxDbWriter>();
builder.Services.AddHostedService<KafkaConsumerWorker>();

var app = builder.Build();

// No ingestion API is exposed — events arrive via Kafka. Only ops health checks are served.
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (IInfluxDBClient client) =>
    await client.PingAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

// Exposed so the test host (WebApplicationFactory) can reference the entry-point assembly.
public partial class Program;
