# Transaction Telemetry Service (TTS)

A lightweight .NET 8 service that **consumes transaction telemetry events from Kafka** and
stores them in InfluxDB for real-time monitoring in Grafana.

**Core principle:** the service is not in the payment path. Transaction events are already
published to Kafka by upstream systems; TTS subscribes to that topic and, for every message,
writes the event to InfluxDB. It exposes **no ingestion API** — Kafka is the durable event source.

## Architecture

```
Upstream systems ──► Kafka topic (transaction-events)
                          │  subscribe
                          ▼
              KafkaConsumerWorker (BackgroundService)
                          │
        IEventTransformer ──► IInfluxWriter (batched, retry)
                          ▼
                      InfluxDB ──► Grafana
```

| Layer | Files |
|-------|-------|
| Consumer | `Workers/KafkaConsumerWorker.cs` — subscribes, deserialises, transforms, writes |
| Models | `Models/TransactionEventDto.cs` — generic event + open `data` bag |
| Transformer | `Services/InfluxEventTransformer.cs` — event → InfluxDB point |
| Writer | `Services/InfluxDbWriter.cs` — batched writes with retry |
| Config | `Configuration/KafkaOptions.cs`, `InfluxDbOptions.cs`, `TelemetryOptions.cs` |
| Host | `Program.cs` — registers the consumer; serves only health endpoints |

## Event format (Kafka message value)

```json
{
  "eventType": "TRANSACTION_CREATED",
  "eventTimestamp": "2026-06-13T10:30:00Z",
  "transactionId": "TXN123456",
  "data": { "amount": 99.5, "currency": "USD" }
}
```

The `data` object is free-form — new transaction attributes need no code change. Malformed or
incomplete messages are logged and skipped. Numbers in `data` are stored as float for a
consistent InfluxDB field type.

## Endpoints

Only operational health checks (no ingestion API):
- `GET /health/live` — process liveness
- `GET /health/ready` — InfluxDB reachability

## Configuration

Bound from `appsettings.json`, overridable by environment variables. See [.env.example](.env.example).

| Setting | Env var | Default |
|---------|---------|---------|
| Kafka brokers | `Kafka__BootstrapServers` | localhost:9092 |
| Kafka topic | `Kafka__Topic` | transaction-events |
| Consumer group | `Kafka__GroupId` | tts-influx-writer |
| Offset reset | `Kafka__AutoOffsetReset` | Earliest |
| Write retries | `Telemetry__RetryCount` | 5 |
| InfluxDB URL / token / org / bucket | `InfluxDb__*` | _(from env)_ |

## Running

### Tests
```bash
dotnet test
```

### Full stack (Kafka + API + InfluxDB + Grafana)
Requires Docker Desktop.
```powershell
Copy-Item .env.example .env   # then edit .env with real secrets
docker compose -f docker/docker-compose.yml --env-file .env up -d --build
```
- Kafka → `kafka:9092` (internal)
- InfluxDB → http://localhost:8086
- Grafana → http://localhost:3000 (dashboard auto-provisioned)

### Publish test events to Kafka
```powershell
.\scripts\produce-events.ps1 -Count 100
```
The consumer writes them to InfluxDB; view them on the Grafana **Transaction Telemetry** dashboard.

### Generate a report
```powershell
.\scripts\generate-report.ps1
```

## Tech stack

.NET 8 · Confluent.Kafka · Apache Kafka (KRaft) · InfluxDB 2.7 · Grafana · Docker
