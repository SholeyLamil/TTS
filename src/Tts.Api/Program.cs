using InfluxDB.Client;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Middleware;
using Tts.Api.Queue;
using Tts.Api.Services;
using Tts.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

// Strongly-typed configuration, bound from appsettings.json sections and overridable
// by environment variables (Telemetry__ApiKey, InfluxDb__Token, ...).
builder.Services.Configure<TelemetryOptions>(builder.Configuration.GetSection(TelemetryOptions.SectionName));
builder.Services.Configure<InfluxDbOptions>(builder.Configuration.GetSection(InfluxDbOptions.SectionName));

builder.Services.AddControllers();

// One InfluxDB client for the whole app.
builder.Services.AddSingleton<IInfluxDBClient>(sp =>
{
    var influx = sp.GetRequiredService<IOptions<InfluxDbOptions>>().Value;
    return new InfluxDBClient(influx.Url, influx.Token);
});

// Ingestion pipeline.
builder.Services.AddSingleton<IEventQueue, InMemoryEventQueue>();
builder.Services.AddSingleton<IEventTransformer, InfluxEventTransformer>();
builder.Services.AddSingleton<IInfluxWriter, InfluxDbWriter>();
builder.Services.AddHostedService<EventProcessingWorker>();

var app = builder.Build();

if (string.IsNullOrEmpty(app.Services.GetRequiredService<IOptions<TelemetryOptions>>().Value.ApiKey))
    app.Logger.LogWarning("Telemetry:ApiKey is not configured — the events API will reject every request (fail closed).");

app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

// Liveness: process is up. Readiness: InfluxDB is reachable. Both are unauthenticated.
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (IInfluxDBClient client) =>
    await client.PingAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

// Exposed so the test host (WebApplicationFactory) can reference the entry-point assembly.
public partial class Program;
