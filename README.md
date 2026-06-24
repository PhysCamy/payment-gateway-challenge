# Instructions for candidates

This is the .NET version of the Payment Gateway challenge. If you haven't already read this [README.md](https://github.com/cko-recruitment/) on the details of this exercise, please do so now. 

## Template structure
```
src/
    PaymentGateway.Api - a skeleton ASP.NET Core Web API
test/
    PaymentGateway.Api.Tests - an empty xUnit test project
imposters/ - contains the bank simulator configuration. Don't change this

.editorconfig - don't change this. It ensures a consistent set of rules for submissions when reformatting code
docker-compose.yml - configures the bank simulator
PaymentGateway.sln
```

Feel free to change the structure of the solution, use a different test library etc.

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