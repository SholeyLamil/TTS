# TTS — Testing & Trials Guide

How to exercise the Transaction Telemetry Service and produce a report.

## 0. Prerequisites

The stack must be running:
```powershell
$env:Path += ";C:\Program Files\Docker\Docker\resources\bin"   # if 'docker' isn't on PATH
docker compose -f docker/docker-compose.yml --env-file .env up -d
docker compose -f docker/docker-compose.yml ps        # all three should be Up
```
Endpoints: API `http://localhost:8080` · InfluxDB `http://localhost:8086` · Grafana `http://localhost:3000`
Credentials are in `.env` (dev defaults: API key `dev-local-key`, Grafana `admin` / `dev-local-grafana-pw`).

---

## 1. Functional tests (does it behave correctly?)

| # | Test | How | Expected |
|---|------|-----|----------|
| F1 | Liveness | `curl.exe http://localhost:8080/health/live` | `200 {"status":"alive"}` |
| F2 | Readiness | `curl.exe http://localhost:8080/health/ready` | `200 {"status":"ready"}` |
| F3 | Reject missing key | POST `/api/events` with no `X-API-Key` | `401` |
| F4 | Reject wrong key | POST with `X-API-Key: wrong` | `401` |
| F5 | Accept valid event | POST with valid key + body | `202 {"message":"received"}` |
| F6 | Reject bad payload | POST valid key, body `{}` (missing required fields) | `400` |
| F7 | Event reaches InfluxDB | run a trial, then query (section 3) | counts match what was sent |
| F8 | Grafana shows data | open dashboard | panels populate |

Quick F3–F6 one-liner:
```powershell
# F5 accept
curl.exe -i -X POST http://localhost:8080/api/events -H "X-API-Key: dev-local-key" -H "Content-Type: application/json" -d '{ "eventType":"TRANSACTION_CREATED","eventTimestamp":"2026-06-11T10:30:00Z","transactionId":"TXN1","data":{} }'
# F6 bad payload -> 400
curl.exe -i -X POST http://localhost:8080/api/events -H "X-API-Key: dev-local-key" -H "Content-Type: application/json" -d '{}'
```

---

## 2. Load / trial runs (generate data for the report)

Use the generator. It simulates full transaction lifecycles with **current timestamps**
and prints latency + acceptance stats.

```powershell
# Small smoke run
./scripts/send-events.ps1 -Count 50

# Standard trial
./scripts/send-events.ps1 -Count 200

# Throughput / stress (no delay)
./scripts/send-events.ps1 -Count 1000

# Throttled, steady drip (e.g. for watching the live dashboard)
./scripts/send-events.ps1 -Count 300 -DelayMs 200
```

Each transaction emits 4–5 events (received → sent → response → completed/failed → [reversed]),
so `-Count 200` ≈ ~800 events. The script reports: events sent, accepted (202), throughput,
and API latency avg / p50 / p95 / max.

---

## 3. Pulling numbers from InfluxDB (for the report)

Run via the influx CLI in the container. Adjust `range(start:-1h)` to your trial window.

```powershell
$tok = "dev-local-influx-token-0123456789"
$q   = { param($flux) docker exec tts-influxdb-1 influx query --token $tok --org tts $flux }

# Total events stored  (note: group() BEFORE sum() to collapse all series into one total)
& $q 'from(bucket:\"transactions\") |> range(start:-1h) |> filter(fn:(r)=> r._field==\"count\") |> group() |> sum()'

# Count by event type
& $q 'from(bucket:\"transactions\") |> range(start:-1h) |> filter(fn:(r)=> r._field==\"count\") |> group(columns:[\"eventType\"]) |> sum()'

# Failure count
& $q 'from(bucket:\"transactions\") |> range(start:-1h) |> filter(fn:(r)=> r._field==\"count\" and r.eventType==\"TRANSACTION_FAILED\") |> group() |> sum()'
```

---

## 4. Viewing in Grafana

1. Open `http://localhost:3000`, log in (`admin` / `dev-local-grafana-pw`).
2. Dashboard → **Transaction Telemetry**.
3. Set the time range (top-right) to **Last 1 hour** (or match your trial window).
4. Panels: throughput (events/min), events by type, total events, failed/reversed.
5. Screenshot for the report.

---

## 5. Report template

```
TTS Trial Report — <date>
Environment: local Docker (API + InfluxDB 2.7 + Grafana), .NET 8

Trial parameters
  Transactions simulated : ___
  Events sent            : ___
  Trial duration         : ___ s

Functional results (F1–F8)
  [ ] Liveness / readiness OK
  [ ] Auth: rejects missing/invalid key (401), accepts valid (202)
  [ ] Validation: bad payload -> 400
  [ ] Events persisted to InfluxDB (counts match)
  [ ] Grafana dashboard populated

Performance
  Accepted (202)   : ___ / ___  (___%)
  Throughput       : ___ events/sec
  API latency avg  : ___ ms
  API latency p95  : ___ ms
  API latency max  : ___ ms

Storage verification (InfluxDB)
  Total events     : ___
  By type          : RECEIVED __ / SENT __ / RESPONSE __ / COMPLETED __ / FAILED __ / REVERSED __

Observations / issues
  - ...

Conclusion
  - Meets success criteria: gateway sends events, 202 returned immediately,
    events written to InfluxDB asynchronously, visualized in Grafana, no request failures.
```

---

## 6. Reset between trials (optional)

To start from a clean database:
```powershell
docker compose -f docker/docker-compose.yml down -v   # -v wipes InfluxDB + Grafana volumes
docker compose -f docker/docker-compose.yml --env-file .env up -d
```
(Grafana dashboard re-provisions automatically on restart.)
