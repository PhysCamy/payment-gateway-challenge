# Payment Gateway

A REST API that allows merchants to process card payments and retrieve payment details. The gateway sits between merchants and an acquiring bank simulator, masking card data and providing a consistent interface.

## Running the application

### Prerequisites
- [Docker](https://www.docker.com/) — runs the whole stack
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — only needed to run the API
  outside Docker or to run the tests

### Run everything with one command

```bash
docker-compose up --build
```

This builds and starts the entire stack. Add `-d` to run it detached.

| Container | Image | Ports | Purpose |
|---|---|---|---|
| `payment-gateway` | built from `Dockerfile` | `5067` | The API itself |
| `bank_simulator` | `mountebank:2.8.1` | `8080` (API), `2525` (admin) | Stubbed acquiring bank, configured via `imposters/` |
| `prometheus` | `prom/prometheus:v2.51.0` | `9090` | Scrapes and stores metrics from the gateway |
| `loki` | `grafana/loki:2.9.0` | `3100` | Receives and stores structured logs pushed by the gateway |
| `grafana` | `grafana/grafana:10.4.0` | `3000` | Dashboards over Prometheus metrics and Loki logs |

The API is available at:

- API: `http://localhost:5067`
- Metrics: `http://localhost:5067/metrics`
- Health: `http://localhost:5067/health`

(`--build` is only needed when the API source has changed; plain `docker-compose up` reuses
the existing image.)

### Running the API outside Docker (optional, for development)

To iterate on the API with a debugger or hot reload, start the supporting services and run the
API on the host:

```bash
docker-compose up -d bank_simulator prometheus loki grafana
cd src/PaymentGateway.Api
dotnet run
```

Run this way the API also exposes HTTPS and Swagger:
- HTTP: `http://localhost:5067`
- HTTPS: `https://localhost:7092`
- Swagger UI: `https://localhost:7092/swagger`

> **Swagger is only available in Development mode.** `dotnet run` picks up
> `Properties/launchSettings.json`, which sets `ASPNETCORE_ENVIRONMENT=Development`
> automatically, so Swagger is enabled with no extra steps. The Docker image runs in
> `Production` mode, so the `/swagger` endpoint is not exposed there.

For Prometheus to scrape the host process, point its target at `host.docker.internal:5067`
in `prometheus/prometheus.yml` (see the note in that file).

### Running the tests

The default run excludes the integration suite (which needs the bank simulator running):

```bash
dotnet test --filter "Category!=Integration"
```

To run the integration tests as well, start the bank simulator first
(`docker-compose up -d bank_simulator`), then:

```bash
dotnet test
```

---

## API Reference

### POST /api/payments — Process a payment

Validates the payment request, forwards it to the acquiring bank, persists the result, and returns the outcome.

#### Request

**Headers**

| Header | Type | Required |
|---|---|---|
| `Idempotency-Key` | string (UUID) | Yes — `400 Bad Request` if absent |

**Body**

```json
{
  "card_number": "2222405343248877",
  "expiry_month": 4,
  "expiry_year": 2025,
  "currency": "GBP",
  "amount": 100,
  "cvv": "123"
}
```

| Field | Type | Rules |
|---|---|---|
| `card_number` | string | Required; 14–19 digits; must pass the Luhn check |
| `expiry_month` | integer | Required; 1–12 |
| `expiry_year` | integer | Required; combined month + year must not be in the past |
| `currency` | string | Required; one of `USD`, `GBP`, `EUR` |
| `amount` | integer | Required; positive integer in minor currency units (e.g. pence, cents) |
| `cvv` | string | Required; 3–4 digits |

#### Responses

**200 OK — Authorized**
```json
{
  "id": "dc71b257-ee60-4b1b-900c-b47fc5833e07",
  "status": "Authorized",
  "last_four_digits": "8877",
  "expiry_month": 4,
  "expiry_year": 2025,
  "currency": "GBP",
  "amount": 100
}
```

**200 OK — Declined**
```json
{
  "id": "dc71b257-ee60-4b1b-900c-b47fc5833e07",
  "status": "Declined",
  "last_four_digits": "8877",
  "expiry_month": 4,
  "expiry_year": 2025,
  "currency": "GBP",
  "amount": 100
}
```

**400 Bad Request — schema or header validation failure**

Returns ASP.NET's default `ValidationProblemDetails` response. No payment ID is generated and nothing is persisted.

**400 Bad Request — domain validation failure**
```json
{
  "status": "Rejected",
  "errors": [
    "card_number is not a valid card number.",
    "cvv must be 3 or 4 characters long."
  ]
}
```

No payment ID is generated and nothing is persisted. The `errors` array lists every rule that the request violated.

**409 Conflict — idempotency key already in flight**

A request with the same `Idempotency-Key` is currently being processed. Retry after a short delay. No payment ID is generated.

**502 Bad Gateway — bank unreachable**

The bank simulator was unavailable (or returned a 503). The payment was not processed and nothing is persisted. Retry with the same `Idempotency-Key` — the key is released after a 502 so resubmission proceeds normally.

---

### GET /api/payments/{id} — Retrieve a payment

Returns the stored outcome of a previously processed payment.

#### Path parameter

| Parameter | Type | Description |
|---|---|---|
| `id` | GUID | The payment identifier returned at processing time |

#### Responses

**200 OK**
```json
{
  "id": "dc71b257-ee60-4b1b-900c-b47fc5833e07",
  "status": "Authorized",
  "last_four_digits": "8877",
  "expiry_month": 4,
  "expiry_year": 2025,
  "currency": "GBP",
  "amount": 100
}
```

**404 Not Found** — no payment exists for the given ID.

---

### GET /health — Liveness probe

Returns `200 OK` if the process and pipeline are responsive. Suitable for use as a container liveness or readiness probe.

---

## Idempotency

`POST /api/payments` supports idempotency via a merchant-supplied `Idempotency-Key` header. Generate a UUID per distinct payment attempt and reuse it on retries. The gateway ensures the bank is contacted exactly once per key — even if the same request is submitted multiple times.

| Outcome | Key state after response | Behaviour on retry |
|---|---|---|
| Authorized or Declined | Completed | Cached response returned immediately; bank not contacted |
| Rejected (domain validation) | Released | Retry with same key proceeds normally |
| 502 Bad Gateway | Released | Retry with same key proceeds normally |

Concurrent requests with the same key: one proceeds to the bank, the other receives `409 Conflict` and should retry after a short delay.

---

## Observability

The gateway emits structured logs (Serilog → Loki) and Prometheus metrics (prometheus-net),
visualised in Grafana. Everything comes up with `docker-compose up`; no extra steps.

| Tool | URL | Purpose |
|---|---|---|
| Raw metrics | `http://localhost:5067/metrics` | Prometheus exposition format, scraped by Prometheus |
| Prometheus | `http://localhost:9090` | Query metrics directly (e.g. `rate(bank_requests_total[1m])`) |
| Grafana | `http://localhost:3000` | Dashboards over both metrics and logs (login `admin` / `admin`) |

Prometheus scrapes the gateway over the compose network at `payment-gateway:5067`. If you run
the API on the host instead (see above), point the target at `host.docker.internal:5067`.

### Grafana dashboard

Data sources (Prometheus + Loki) and the **Payment Gateway** dashboard are provisioned
automatically — no manual setup. Open Grafana, log in, and find it under
**Dashboards → Payment Gateway**. It has four panels:

| Panel | Source | Shows |
|---|---|---|
| API Request Rate | Prometheus | Requests/sec per route and HTTP status code |
| Bank Request Outcomes | Prometheus | Bank calls/sec by outcome (`authorized`, `declined`, `unreachable`) |
| Response Status Distribution | Prometheus | Pie chart of HTTP status codes over the last 5 minutes |
| Application Logs | Loki | Live structured log stream, newest first |

### Metrics exposed

| Metric | Type | Labels | Source |
|---|---|---|---|
| `http_requests_received_total` | Counter | `method`, `route`, `code` | `UseHttpMetrics()` |
| `http_request_duration_seconds` | Histogram | `method`, `route`, `code` | `UseHttpMetrics()` |
| `bank_requests_total` | Counter | `outcome` | `BankService` |

### Logs

Structured events are written to the console and pushed to Loki under the label
`app="payment-gateway"`. In Grafana's **Explore** view, select the Loki data source and query
`{app="payment-gateway"}`. Key events: bank request dispatched/received, bank unreachable,
payment persisted, and payment rejected. Per PCI, card numbers and CVVs are never logged —
only `last_four_digits`.

The Loki endpoint defaults to `http://localhost:3100`; override it with the
`Observability__LokiUri` environment variable (or the `Observability:LokiUri` config key).
