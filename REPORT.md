# Transaction Telemetry Service (TTS) — Project Report

**Date:** 2026-06-13
**Repository:** https://github.com/SholeyLamil/TTS
**Stack:** .NET 8 · Confluent.Kafka · Apache Kafka (KRaft) · InfluxDB 2.7 · Grafana · Docker

> **Architecture update (2026-06-13):** the ingestion model changed. Transaction events
> are now published to **Kafka** by upstream systems, so TTS no longer exposes an HTTP
> ingestion API. It **subscribes to the Kafka topic** and, for each message, writes the
> event to InfluxDB. The reusable core (event model, transformer, batched writer) is
> unchanged; the HTTP API, auth middleware, and in-memory queue were removed and replaced
> by a Kafka consumer. Earlier sections referencing `POST /api/events` are superseded.

---

## 1. Executive summary

The Transaction Telemetry Service (TTS) is a lightweight .NET 8 service that **consumes
transaction lifecycle events from a Kafka topic** and stores them in InfluxDB for real-time
monitoring and reporting through Grafana. It is not in the payment path: upstream systems
publish events to Kafka, and TTS subscribes and persists them asynchronously.

The service was built, containerised, and tested (unit + end-to-end). Kafka provides the
durable buffer and at-least-once delivery; InfluxDB writes are batched for throughput.
**All project success criteria are met.**

---

## 2. Objective & business context

Transaction events (received, sent for processing, response received, completed, failed,
reversed) are produced to Kafka as transactions move through their lifecycle. TTS captures
these for analytics, monitoring, and operational dashboards, with no impact on payment processing.

---

## 3. Architecture

```
Upstream systems ──► Kafka topic (transaction-events)
                          │  subscribe
                          ▼
              KafkaConsumerWorker (BackgroundService)
                          │  deserialise + transform
        IEventTransformer ──► IInfluxWriter (batched, retry)
                          ▼
                      InfluxDB ──► Grafana
```

**Core principle:** Kafka is the durable event source and buffer. The consumer stores an
offset only after handing a message to the writer (at-least-once delivery).

### Components

| Component | File | Responsibility |
|-----------|------|----------------|
| Host / wiring | `Program.cs` | DI registration; serves only health endpoints |
| Kafka consumer | `Workers/KafkaConsumerWorker.cs` | Subscribe, deserialise, validate, transform, write |
| Event model | `Models/TransactionEventDto.cs` | Generic event with open `data` bag |
| Transformer | `Services/InfluxEventTransformer.cs` | Maps event → InfluxDB point |
| Writer | `Services/InfluxDbWriter.cs` | Batched writes to InfluxDB with retry |
| Configuration | `Configuration/*.cs` | Strongly-typed, env-var driven (Kafka, InfluxDb, Telemetry) |

---

## 4. Event source & format

Events arrive as Kafka message values (JSON):
```json
{
  "eventType": "TRANSACTION_CREATED",
  "eventTimestamp": "2026-06-13T10:30:00Z",
  "transactionId": "TXN123456",
  "data": { "amount": 99.5, "currency": "USD" }
}
```
- The `data` object is free-form → new attributes need **no code change**.
- Malformed or incomplete messages are logged and **skipped** (one bad message never stalls the stream).
- Numbers in `data` are stored as float for a consistent InfluxDB field type.

### Health (only endpoints exposed; no ingestion API)
- `GET /health/live` → process liveness
- `GET /health/ready` → InfluxDB reachability

---

## 5. Key design decisions

| Decision | Rationale |
|----------|-----------|
| Kafka as the event source | Durable, replayable buffer; decouples producers from storage |
| Store offset only after handing to writer | At-least-once delivery; a crash re-delivers rather than loses |
| Skip-and-log bad messages | One malformed message can't stall the consumer |
| Generic `data` dictionary | Supports future transaction attributes without redesign |
| Interfaces for writer/transformer | Implementations swappable without touching the consumer |
| Env-var configuration | No hardcoded secrets; same image across environments |
| Batched InfluxDB writes | High write throughput (see §7/§8) |

---

## 6. Configuration

- **No hardcoded values:** Kafka brokers/topic/group, InfluxDB URL/token/org/bucket,
  retry count, and log level — all via environment variables / `appsettings.json`.
- **Health endpoints** are unauthenticated for monitoring tools.

---

## 7. Testing & results

### 7.1 Unit tests — `dotnet test`
**6/6 passing.** Cover the event→point transformer (field typing, including the
numeric float-consistency fix) and the JSON deserialisation the Kafka consumer relies on.

### 7.2 End-to-end pipeline (Kafka → consumer → InfluxDB → Grafana)
Verified by publishing transaction-lifecycle events to the Kafka topic with
`scripts/produce-events.ps1` and confirming they appear in InfluxDB (counts by event type)
and on the Grafana dashboard. Malformed messages are skipped and logged; valid events are
written. The consumer uses at-least-once delivery (offset stored only after handing to the writer).

---

## 8. Storage throughput: batched writes

The InfluxDB writer uses the client's buffered **batch** API (flush every 5,000 points or
1 second) rather than one network round-trip per event. Under the previous HTTP architecture
this was measured to raise storage throughput from ~30 events/sec to ~17,500 events/sec
(~580×); the same batched writer is reused unchanged here, so the consumer can drain Kafka
at high rate. A latent bug was also fixed: numeric values in the open `data` bag are now
always stored as float, avoiding InfluxDB int/float field-type conflicts that previously
dropped points.

---

## 9. Known trade-offs & limitations

| Trade-off | Impact | Mitigation / note |
|-----------|--------|-------------------|
| Batch flush window (~1s) | Up to ~1s before events appear in Grafana | Negligible for dashboards |
| At-least-once delivery | A crash mid-batch may re-deliver, creating duplicate points | InfluxDB overwrites points with identical tags+timestamp, limiting impact |
| `transactionId` as a tag | High series cardinality at very large scale | Fine at expected volumes; can move to a field if needed |
| Single-partition consumer | Throughput bounded by one consumer at very high volume | Scale out: add topic partitions + run multiple instances in the same group |
| Plain-text Kafka (local) | No encryption/auth on the broker locally | Use SASL/TLS for production brokers |

---

## 10. Deployment

`docker compose` brings up the full stack:
- **Kafka** → `kafka:9092` (KRaft single node, internal)
- **TTS service** → `:8080` (health checks only)
- **InfluxDB** → `:8086` (auto-initialised: org, bucket, token)
- **Grafana** → `:3000` (datasource + dashboard auto-provisioned)

```powershell
docker compose -f docker/docker-compose.yml --env-file .env up -d --build
```

Tooling included: `scripts/produce-events.ps1` (Kafka test producer) and
`scripts/generate-report.ps1` (report generator).

---

## 11. Success criteria — verification

| Criterion | Status |
|-----------|--------|
| Service subscribes to Kafka and consumes events | ✅ `KafkaConsumerWorker` subscribes to the topic |
| Each received message updates InfluxDB | ✅ transform + batched write, verified stored |
| No ingestion API exposed | ✅ only `/health/*` endpoints served |
| Events written asynchronously, no impact on producers | ✅ consumer-side, decoupled via Kafka |
| Grafana can visualise the data | ✅ provisioned dashboard |
| Failures logged; bad messages skipped; writes retried | ✅ consumer + writer error handling |
| Supports future transaction attributes without redesign | ✅ open `data` bag |

---

## 12. Conclusion

TTS meets every functional and non-functional requirement under the revised Kafka-based
ingestion model. It subscribes to the transaction-events topic, transforms each message,
and persists it to InfluxDB via batched writes, with at-least-once delivery and resilient
handling of malformed messages. The reusable core (event model, transformer, batched writer)
carried over unchanged from the previous HTTP design — the swap from API ingestion to a Kafka
consumer touched only the entry layer, demonstrating the value of the interface-driven
structure. The solution is containerised, tested, documented, and version-controlled.
