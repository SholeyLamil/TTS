// k6 load test for the Transaction Telemetry Service.
// Ramps concurrent virtual users to find the endpoint's throughput + latency limits.
//
// Run (from repo root, stack already up):
//   Get-Content scripts/load-test.js | docker run --rm -i --network tts_default `
//     -e API_URL=http://tts-api:8080 -e API_KEY=dev-local-key grafana/k6 run -
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const accepted = new Counter('accepted_202');

const API = __ENV.API_URL || 'http://tts-api:8080';
const KEY = __ENV.API_KEY || 'dev-local-key';

const TYPES = [
  'TRANSACTION_RECEIVED',
  'TRANSACTION_SENT_FOR_PROCESSING',
  'TRANSACTION_RESPONSE_RECEIVED',
  'TRANSACTION_COMPLETED',
  'TRANSACTION_FAILED',
  'TRANSACTION_REVERSED',
];

export const options = {
  scenarios: {
    ramp: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '15s', target: 50 },   // warm up
        { duration: '20s', target: 200 },   // moderate
        { duration: '20s', target: 500 },   // heavy
        { duration: '10s', target: 0 },     // ramp down
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],     // <1% non-2xx
    http_req_duration: ['p(95)<200'],   // 95% under 200ms
  },
};

export default function () {
  const t = TYPES[Math.floor(Math.random() * TYPES.length)];
  const body = JSON.stringify({
    eventType: t,
    eventTimestamp: new Date().toISOString(),
    transactionId: 'TXN' + Math.floor(Math.random() * 1000000),
    data: { amount: Math.round(Math.random() * 500000) / 100, currency: 'USD' },
  });

  const res = http.post(`${API}/api/events`, body, {
    headers: { 'Content-Type': 'application/json', 'X-API-Key': KEY },
  });

  check(res, { 'status is 202': (r) => r.status === 202 });
  if (res.status === 202) accepted.add(1);
}
