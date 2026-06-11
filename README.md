# Transaction Telemetry Service (TTS)

A lightweight .NET 8 API that captures transaction telemetry events from a payment
gateway and stores them in InfluxDB for real-time monitoring in Grafana.

**Core principle:** the payment gateway must never wait for a database. The API accepts
an event, drops it on an in-memory queue, returns `202 Accepted` immediately, and a
background worker writes to InfluxDB at its own pace with retries.

## Architecture

```
Payment Gateway
      │  POST /api/events  (X-API-Key)
      ▼
ApiKeyMiddleware ──► EventsController ──► IEventQueue (Channel<T>)
                                              │  202 returned here
                                              ▼
                          EventProcessingWorker (BackgroundService)
                                              │
              IEventTransformer ──► IInfluxWriter (retry + backoff)
                                              ▼
                                          InfluxDB ──► Grafana
```

| Layer | Files |
|-------|-------|
| Middleware | `Middleware/ApiKeyMiddleware.cs` — `X-API-Key` gate, fail-closed |
| Controller | `Controllers/EventsController.cs` — `POST /api/events` → 202 |
| Models | `Models/TransactionEventDto.cs` — generic event + open `data` bag |
| Queue | `Queue/IEventQueue.cs`, `Queue/InMemoryEventQueue.cs` |
| Worker | `Workers/EventProcessingWorker.cs` |
| Services | `Services/IEventTransformer.cs`, `InfluxEventTransformer.cs`, `IInfluxWriter.cs`, `InfluxDbWriter.cs` |
| Config | `Configuration/TelemetryOptions.cs`, `InfluxDbOptions.cs` |

## API

`POST /api/events` — header `X-API-Key: <key>`

```json
{
  "eventType": "TRANSACTION_CREATED",
  "eventTimestamp": "2026-06-11T10:30:00Z",
  "transactionId": "TXN123456",
  "data": { "amount": 99.5, "currency": "USD" }
}
```

Returns `202 Accepted` with `{ "message": "received" }`. The `data` object is free-form —
new transaction attributes need no code change.

Health: `GET /health/live`, `GET /health/ready` (InfluxDB ping) — both unauthenticated.

## Configuration

Bound from `appsettings.json`, overridable by environment variables
(`Telemetry__ApiKey`, `InfluxDb__Token`, ...). See [.env.example](.env.example).

| Setting | Env var | Default |
|---------|---------|---------|
| API key | `Telemetry__ApiKey` | _(empty → rejects all)_ |
| Queue size | `Telemetry__QueueSize` | 10000 |
| Retry count | `Telemetry__RetryCount` | 5 |
| InfluxDB URL | `InfluxDb__Url` | http://localhost:8086 |
| InfluxDB token / org / bucket | `InfluxDb__Token` / `__Organisation` / `__Bucket` | _(empty)_ |

## Running

### Tests
```bash
dotnet test
```

### API locally
```powershell
$env:Telemetry__ApiKey = "test-key"
dotnet run --project src/Tts.Api
```

### Full stack (API + InfluxDB + Grafana)
Requires Docker Desktop.
```powershell
Copy-Item .env.example .env   # then edit .env with real secrets
docker compose -f docker/docker-compose.yml --env-file .env up --build
```
- API → http://localhost:8080
- InfluxDB → http://localhost:8086
- Grafana → http://localhost:3000 (dashboard auto-provisioned)

## Tech stack

.NET 8 · ASP.NET Core · InfluxDB 2.7 · Grafana · Docker
