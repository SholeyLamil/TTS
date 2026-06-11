# Transaction Telemetry Service (TTS) — Project Report

**Date:** 2026-06-11
**Repository:** https://github.com/SholeyLamil/TTS
**Stack:** .NET 8 · ASP.NET Core · InfluxDB 2.7 · Grafana · Docker

---

## 1. Executive summary

The Transaction Telemetry Service (TTS) is a lightweight .NET 8 API that ingests
transaction lifecycle events from a payment gateway and stores them in InfluxDB for
real-time monitoring and reporting through Grafana. Its defining requirement is that
**telemetry must never slow down or affect payment processing** — so it accepts events,
queues them in memory, acknowledges immediately (`202 Accepted`), and writes to the
database asynchronously via a background worker.

The service was built, containerised, tested (functional, unit, and load), and a
performance bottleneck in the storage layer was identified through load testing and
resolved. **All project success criteria are met.**

---

## 2. Objective & business context

A payment gateway emits events as transactions move through their lifecycle
(received, sent for processing, response received, completed, failed, reversed).
TTS captures these for analytics, monitoring, and operational dashboards. It is **not**
part of the payment flow and must impose zero latency or failure risk on payments.

---

## 3. Architecture

```
Payment Gateway
      │  POST /api/events  (X-API-Key)
      ▼
ApiKeyMiddleware ──► EventsController ──► IEventQueue (in-memory Channel<T>)
   (auth gate)        (validate, 202)          │  202 returned here, instantly
                                               ▼
                              EventProcessingWorker (BackgroundService)
                                               │
                       IEventTransformer ──► IInfluxWriter (batched, retry)
                                               ▼
                                           InfluxDB ──► Grafana
```

**Core principle:** accept fast, process separately. The payment gateway never waits
for the database.

### Components

| Component | File | Responsibility |
|-----------|------|----------------|
| API entry / wiring | `Program.cs` | DI registration, middleware, health checks |
| Security gate | `Middleware/ApiKeyMiddleware.cs` | Validates `X-API-Key`, fail-closed, constant-time |
| Ingestion endpoint | `Controllers/EventsController.cs` | `POST /api/events`, validate, queue, return 202 |
| Event model | `Models/TransactionEventDto.cs` | Generic event with open `data` bag |
| Queue (interface) | `Queue/IEventQueue.cs` | Contract — enables future Kafka/RabbitMQ swap |
| Queue (impl) | `Queue/InMemoryEventQueue.cs` | Bounded `Channel<T>`, drops + logs when full |
| Background worker | `Workers/EventProcessingWorker.cs` | Drains queue, transforms, writes |
| Transformer | `Services/InfluxEventTransformer.cs` | Maps event → InfluxDB point |
| Writer | `Services/InfluxDbWriter.cs` | Batched writes to InfluxDB with retry |
| Configuration | `Configuration/*.cs` | Strongly-typed, env-var driven |

---

## 4. API surface

### `POST /api/events`
Header: `X-API-Key: <key>`
```json
{
  "eventType": "TRANSACTION_CREATED",
  "eventTimestamp": "2026-06-11T10:30:00Z",
  "transactionId": "TXN123456",
  "data": { "amount": 99.5, "currency": "USD" }
}
```
- `202 Accepted` → `{ "message": "received" }` (queued)
- `400 Bad Request` → invalid/malformed payload
- `401 Unauthorized` → missing/invalid API key

The `data` object is free-form, so new transaction attributes require **no code changes**.

### Health
- `GET /health/live` → process liveness
- `GET /health/ready` → InfluxDB reachability (unauthenticated, for orchestrators)

---

## 5. Key design decisions

| Decision | Rationale |
|----------|-----------|
| In-memory `Channel<T>` queue | Near-instant hand-off; decouples ingestion from storage |
| Return 202 before DB write | Payments never wait on the database |
| Drop-on-full (bounded queue) | A flood can never exhaust memory or block the API |
| Interfaces for queue/writer/transformer | Swap implementations (e.g. Kafka) without touching the API |
| Generic `data` dictionary | Supports future transaction attributes without redesign |
| Env-var configuration | No hardcoded secrets; same image across environments |
| Constant-time, fail-closed auth | Secure by default; no key configured = reject all |
| Batched InfluxDB writes | High write throughput (see §7) |

---

## 6. Security & configuration

- **Authentication:** shared secret in `X-API-Key`, compared in constant time, fail-closed.
- **Configuration (no hardcoded values):** API key, queue size, retry count, InfluxDB
  URL/token/org/bucket, and log level — all via environment variables / `appsettings.json`.
- **Health endpoints** are intentionally unauthenticated for monitoring tools.

---

## 7. Testing & results

### 7.1 Unit tests — `dotnet test`
**9/9 passing.** Cover the bounded queue (drop behaviour), the event→point transformer
(field typing), and the API-key authenticator (accept/reject/fail-closed).

### 7.2 Functional checks (live probe, clean database)
| Check | Expected | Result |
|-------|----------|--------|
| Accept valid event | 202 | **202 PASS** (100/100) |
| Reject invalid API key | 401 | **401 PASS** |
| Reject malformed payload | 400 | **400 PASS** |
| Latency p95 | low | **27.5 ms** |

### 7.3 Load test — k6, ramped 0→500 concurrent users over 65s

| Metric | Result |
|--------|--------|
| Total requests | **1,147,294** |
| Throughput | **~17,650 requests/sec** |
| HTTP success | **100.00%** (zero failures) |
| Latency avg / p95 / max | 9.2 ms / **25.8 ms** / 252 ms |
| API CPU / memory | ~idle / ~450 MB |

**Conclusion:** the endpoint sustains 17k+ requests/sec at sub-30ms p95 with zero
failures and trivial CPU. It comfortably meets the "never slow down payments" requirement.

---

## 8. Performance improvement: batched writes

Load testing revealed the **storage writer** — not the endpoint — as the bottleneck.
The original writer issued one network round-trip per event (~30 events/sec), so under
sustained overload the bounded queue overflowed and most events were dropped (by design,
to protect the endpoint).

**Fix:** switched the writer to the InfluxDB client's buffered **batch** API (flush every
5,000 points or 1 second), isolated entirely behind the `IInfluxWriter` interface.

| Metric (under identical load) | Before | After |
|-------------------------------|--------|-------|
| Storage throughput | ~30 events/sec | **~17,500 events/sec** |
| Persisted-under-load rate | ~1% | **~99%** |
| Endpoint latency / success | unchanged | unchanged |

A secondary latent bug was found and fixed during this work: numeric values in the open
`data` bag were sometimes stored as integer and sometimes as float, causing InfluxDB
field-type conflicts that silently dropped points. Numeric fields are now consistently
stored as float.

---

## 9. Known trade-offs & limitations

| Trade-off | Impact | Mitigation / note |
|-----------|--------|-------------------|
| In-memory queue | Buffered events lost if the process crashes | Acceptable for non-critical telemetry; `IEventQueue` allows a durable queue (Kafka/Redis) later |
| Drop-on-overload | Events dropped if sustained load exceeds writer rate | Far less likely after batching; logged when it happens |
| Batch flush window (~1s) | Up to ~1s before events appear in Grafana | Negligible for dashboards |
| `transactionId` as a tag | High series cardinality at very large scale | Fine at expected volumes; can move to a field if needed |
| Single static API key | No per-client keys / rotation; plain HTTP locally | Add TLS + per-client keys for production ingress |

---

## 10. Deployment

`docker compose` brings up the full stack:
- **API** → `:8080`
- **InfluxDB** → `:8086` (auto-initialised: org, bucket, token)
- **Grafana** → `:3000` (datasource + dashboard auto-provisioned)

```powershell
docker compose -f docker/docker-compose.yml --env-file .env up -d --build
```

Tooling included: `scripts/send-events.ps1` (trial generator), `scripts/load-test.js`
(k6 load test), `scripts/generate-report.ps1` (report generator).

---

## 11. Success criteria — verification

| Criterion | Status |
|-----------|--------|
| Gateway can send transaction events | ✅ `POST /api/events` → 202 |
| Events accepted without affecting payment performance | ✅ p95 ~26 ms, fire-and-forget |
| Events written to InfluxDB asynchronously | ✅ background worker, verified stored |
| Grafana can visualise the data | ✅ provisioned dashboard |
| Failures logged and retried | ✅ retry + logging in writer |
| Supports future transaction attributes without redesign | ✅ open `data` bag |
| Security via API key | ✅ enforced (401 on bad key) |

---

## 12. Conclusion

TTS meets every functional and non-functional requirement. The endpoint is highly
performant (17k+ req/sec, sub-30ms p95, zero failures) and correctly prioritises payment
safety through asynchronous, fire-and-forget ingestion. Load testing surfaced and resolved
a storage-throughput bottleneck (≈580× improvement via batching) and a latent field-type
bug. The interface-driven design leaves clean upgrade paths (durable queue, TLS,
per-client auth) for future production hardening. The solution is containerised, tested,
documented, and version-controlled.
