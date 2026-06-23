# Payment Gateway — Technical Specification

## Overview

A REST API that allows merchants to process card payments and retrieve payment details. The gateway sits between merchants and an acquiring bank simulator, masking card data and providing a consistent interface.

---

## Architecture

```
Merchant → PaymentGateway.Api → Bank Simulator (Mountebank, port 8080)
                    ↓
             PaymentsRepository (in-memory)
```

**Key principles:**
- No database; in-memory storage is sufficient
- Full card numbers must never be persisted, returned, or logged (PCI compliance)
- The gateway is the source of truth for payment IDs and status

---

## Project Structure

```
src/
  PaymentGateway.Api/
    Controllers/
      PaymentsController.cs
    Interfaces/
      IBankService.cs
      IPaymentsRepository.cs
      IPaymentRequestValidator.cs
    Models/
      Requests/
        PostPaymentRequest.cs
      Responses/
        PostPaymentResponse.cs
      Enums/
        PaymentStatus.cs
        Currency.cs
    Services/
      PaymentsRepository.cs
      BankService.cs
    Validators/
      PostPaymentRequestValidator.cs
    Program.cs
test/
  PaymentGateway.Api.Tests/
    PaymentsControllerTests.cs
```

---

## API Endpoints

### POST /api/payments — Process a Payment

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

| Field | Type | Validation |
|---|---|---|
| `card_number` | string | Required; 14–19 characters (enforced at schema level); digits only (enforced at domain level) |
| `expiry_month` | integer | Required; 1–12 (enforced at schema level) |
| `expiry_year` | integer | Required; validated via combined month+year check at domain level |
| `currency` | string | Required; one of `USD`, `GBP`, `EUR` |
| `amount` | integer | Required; positive integer in minor currency units (e.g. pence, cents) |
| `cvv` | string | Required; 3–4 digits, numeric only |

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

**400 Bad Request — missing `Idempotency-Key` header**

Returns ASP.NET's default `ValidationProblemDetails` response. No ID is generated and nothing is persisted.

**400 Bad Request — Layer 1 failure (schema/model binding)**

Returns ASP.NET's default `ValidationProblemDetails` response. No ID is generated and nothing is persisted.

**400 Bad Request — Layer 2 failure (domain validation)**
```json
{
  "status": "Rejected"
}
```

No ID is generated and nothing is persisted.

**409 Conflict — idempotency key already in flight**

A request with the same `Idempotency-Key` is currently being processed. The merchant should retry after a short delay. No ID is generated and nothing is persisted.

---

### GET /api/payments/{id} — Retrieve a Payment

Returns the stored outcome of a previously processed payment.

#### Path Parameter

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

## Currency Enum

```csharp
public enum Currency { USD, GBP, EUR }
```

The `currency` field on `PostPaymentRequest` is typed as `Currency`. ASP.NET model binding deserialises the string value directly to the enum, returning `400` automatically if the value is missing or not one of the three allowed codes. No regex or layer 2 check is needed.

---

## Payment Status Model

```csharp
public enum PaymentStatus { Authorized, Declined, Rejected }
```

| Value | Meaning |
|---|---|
| `Authorized` | Bank approved the payment |
| `Declined` | Bank declined the payment |
| `Rejected` | Gateway rejected before reaching the bank (validation failure) |

The `status` field on `PostPaymentResponse` is typed as `PaymentStatus`. The JSON serialiser must be configured to serialise enums as strings (not integers) so that responses contain `"Authorized"` rather than `0`, and so that the `Currency` enum on the request deserialises from its string form correctly.

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    });
```

---

## Bank Simulator Integration

The gateway forwards valid payment requests to the Mountebank bank simulator running on `http://localhost:8080`.

### Request to Bank

```
POST http://localhost:8080/payments
Content-Type: application/json

{
  "card_number": "2222405343248877",
  "expiry_date": "04/2025",
  "currency": "GBP",
  "amount": 100,
  "cvv": "123"
}
```

Note: `expiry_date` is formatted as `MM/YYYY`.

### Bank Response

```json
{
  "authorized": true,
  "authorization_code": "0e23c390-8175-4e22-98e8-e541de957b7e"
}
```

### Simulator Behaviour

| Card last digit | Bank response | Gateway status |
|---|---|---|
| 1, 3, 5, 7, 9 | `200 authorized: true` | `Authorized` |
| 2, 4, 6, 8 | `200 authorized: false` | `Declined` |
| 0 | `503 Service Unavailable` | Return `502 Bad Gateway` — bank was unreachable; the payment was not processed |
| Missing fields | `400 Bad Request` | Should not occur if gateway validates first |

**PCI note:** The full card number is forwarded to the bank as part of the request payload. It must not be captured in any application-level logging (e.g., request/response logging middleware must not log the bank request body).

### Interface and HttpClient Configuration

`IBankService` defines the contract for bank communication:

```csharp
public interface IBankService
{
    Task<BankSimulatorResponse> ProcessPaymentAsync(BankSimulatorRequest request);
}
```

`BankService` implements `IBankService` and is registered as a typed `HttpClient` via `IHttpClientFactory`. The base address is read from `appsettings.json`:

```json
{
  "BankSimulator": {
    "BaseUrl": "http://localhost:8080"
  }
}
```

`PaymentsController` depends on `IBankService`, not on `BankService` directly.

---

## Validation

Validation runs in two layers. Both return `400 Bad Request` but with different response shapes, and neither persists a payment record.

### Layer 1 — Schema Validation (ASP.NET Model Binding + Data Annotations)

Applied automatically by the framework before the controller action runs. Handles structural correctness and type safety.

| Field | Constraint | Annotation |
|---|---|---|
| `card_number` | Required; string length 14–19 | `[Required]`, `[StringLength(19, MinimumLength = 14)]` |
| `expiry_month` | Required; integer 1–12 | `[Required]`, `[Range(1, 12)]` |
| `expiry_year` | Required; integer | `[Required]` — validated against current date via combined month+year check at layer 2 |
| `currency` | Required; must be a valid `Currency` enum value (`USD`, `GBP`, `EUR`) | Type as `Currency` enum — model binding rejects unknown values automatically |
| `amount` | Required; integer ≥ 1 | `[Required]`, `[Range(1, int.MaxValue)]` |
| `cvv` | Required; string length 1–50 | `[Required]`, `[StringLength(50, MinimumLength = 1)]` |

If model binding fails, ASP.NET returns `400` automatically via `[ApiController]`. The controller does not need to handle this case explicitly.

Unknown fields in the request body must also be rejected with `400` rather than silently ignored. Both this and enum string serialisation are configured together in `Program.cs` — see the JSON configuration block in the Payment Status Model section below.

### Layer 2 — Domain Validation (`PostPaymentRequestValidator`)

Applied inside the controller after model binding succeeds. `IPaymentRequestValidator` defines the contract; `PostPaymentRequestValidator` is the sole implementation. The controller depends on `IPaymentRequestValidator`, not on the concrete class.

```csharp
public interface IPaymentRequestValidator
{
    bool IsValid(PostPaymentRequest request);
}
```

Enforces business and format rules:

| Field | Rule |
|---|---|
| `card_number` | Must be 14–19 characters; all characters must be digits; must pass the Luhn algorithm |
| `expiry_month` | Validated at layer 1 via `[Range(1, 12)]` — no additional layer 2 check needed |
| `expiry_year` + `expiry_month` | Combined month and year must be greater than or equal to the current system month and year |
| `currency` | Validated at layer 1 via enum binding — no additional layer 2 check needed |
| `amount` | Validated at layer 1 via `[Range(1, int.MaxValue)]` — no additional layer 2 check needed |
| `cvv` | Must be 3–4 characters; all characters must be digits |

Layer 2 failures return `{"status": "Rejected"}` with no ID. Nothing is persisted.

#### Luhn Algorithm

The Luhn check is applied after confirming the card number is 14–19 numeric digits. Use the `LuhnDotNet` NuGet package:

```csharp
Luhn.IsValid(cardNumber) // returns true if valid
```

Call this inside `PostPaymentRequestValidator` — no custom implementation needed.

---

## In-Memory Storage

`IPaymentsRepository` defines the storage contract. `PaymentsRepository` is the sole implementation, registered as a singleton.

```csharp
public interface IPaymentsRepository
{
    bool TryBeginProcessing(string idempotencyKey);
    void Add(PostPaymentResponse payment, string idempotencyKey);
    void CancelProcessing(string idempotencyKey);
    PostPaymentResponse? Get(Guid id);
    PostPaymentResponse? GetByIdempotencyKey(string idempotencyKey);
}
```

`PaymentsController` depends on `IPaymentsRepository`, not on `PaymentsRepository` directly.

`PaymentsRepository` maintains three internal `ConcurrentDictionary` instances:

```csharp
private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();
private readonly ConcurrentDictionary<string, PostPaymentResponse> _completed = new();
private readonly ConcurrentDictionary<string, byte> _inFlight = new();
```

`_payments` is keyed by payment ID. `_completed` and `_inFlight` together implement the idempotency key lifecycle — see the Idempotency section for details.

**PCI note:** `PostPaymentResponse` is the stored type by design — it holds only `last_four_digits`, never the full card number. This is the primary enforcement point ensuring the full PAN is never written to the repository. Do not change the stored type to `PostPaymentRequest` or add a full card number field to `PostPaymentResponse`.

---

## Idempotency

`POST /api/payments` supports idempotency via a merchant-supplied `Idempotency-Key` header. Merchants generate a UUID per distinct payment attempt and reuse it on retries. The gateway ensures the bank is contacted exactly once per key — even if the merchant submits the same request multiple times.

### Key Lifecycle

A key moves through three states:

| State | Meaning |
|---|---|
| **Unclaimed** | Key has never been seen |
| **In-flight** | A request with this key is currently being processed |
| **Completed** | A terminal result (`Authorized` or `Declined`) has been stored against the key |

### Controller Flow

```
Receive POST /api/payments
    │
    ├─ Idempotency-Key header missing? → 400 Bad Request
    │
    ├─ Key completed? → 200 OK (return cached response; bank not contacted)
    │
    ├─ Key in-flight? → 409 Conflict (merchant should retry after a short delay)
    │
    ├─ Claim key as in-flight [TryBeginProcessing]
    │
    ├─ Layer 2 validation fails? → CancelProcessing; 400 {"status": "Rejected"}
    │
    ├─ Call bank
    │     ├─ Authorized or Declined → Add(response, key); 200 OK
    │     └─ Unreachable (502/timeout) → CancelProcessing; 502 Bad Gateway
    │
    └─ Unexpected exception → CancelProcessing; rethrow
```

Only `Authorized` and `Declined` outcomes are stored against the key. `Rejected` and `502` responses release the key without storing anything, so the merchant can correct the request (for `Rejected`) or retry (for `502`) with the same key.

### Repository Method Contracts

| Method | Behaviour |
|---|---|
| `TryBeginProcessing(key)` | Adds `key` to `_inFlight` via `TryAdd`; returns `false` if already in-flight or completed |
| `Add(payment, key)` | Writes to `_payments` and `_completed`; removes from `_inFlight` |
| `CancelProcessing(key)` | Removes from `_inFlight`; leaves `_completed` untouched |
| `GetByIdempotencyKey(key)` | Returns the stored response from `_completed`, or `null` if not found |

`TryBeginProcessing` must check both `_inFlight` and `_completed` before claiming — a key that has already completed must not be claimable again. The check-then-claim is safe because `ConcurrentDictionary.TryAdd` is atomic: only one concurrent caller can succeed.

### Header Enforcement

Declare `Idempotency-Key` as a required action parameter so `[ApiController]` returns `400` automatically if it is absent:

```csharp
public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync(
    PostPaymentRequest request,
    [FromHeader(Name = "Idempotency-Key")] string idempotencyKey)
```

---

## Concurrency

ASP.NET Core handles concurrent requests on multiple threads. Every component must be safe under concurrent access.

| Component | Approach |
|---|---|
| `PaymentsRepository` | Singleton backed by `ConcurrentDictionary` — all read/write operations are atomic with no additional locking required |
| `BankService` | Registered as a typed `HttpClient` via `IHttpClientFactory` — the factory manages connection pooling and `HttpClient` instances are safe to use concurrently; the client class must hold no mutable instance state |
| `PaymentsController` | Transient (ASP.NET default) — instantiated per request; must hold no mutable instance state |
| `PostPaymentRequestValidator` | Stateless — safe to register as a singleton or instantiate per request |
| All I/O | Controller actions and the bank client must be fully `async`/`await` throughout to avoid blocking thread-pool threads under load |

---

## Resilience and Fault Tolerance

### Rate Limiting

Rate limiting is applied at the API level using ASP.NET Core's built-in rate limiter (`Microsoft.AspNetCore.RateLimiting` — no additional package required). Limits are enforced per client IP using a fixed window policy. When the limit is exceeded, the gateway returns `429 Too Many Requests` immediately with no queuing.

Configure in `Program.cs`:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("post-payments", opts =>
    {
        opts.Window = TimeSpan.FromMinutes(1);
        opts.PermitLimit = 60;
        opts.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("get-payments", opts =>
    {
        opts.Window = TimeSpan.FromMinutes(1);
        opts.PermitLimit = 300;
        opts.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

app.UseRateLimiter();
```

Apply the policies to each action via `[EnableRateLimiting]`:

```csharp
[HttpPost]
[EnableRateLimiting("post-payments")]
public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync(...)

[HttpGet("{id:guid}")]
[EnableRateLimiting("get-payments")]
public ActionResult<PostPaymentResponse> GetPayment(Guid id)
```

#### Limits

| Endpoint | Limit | Rationale |
|---|---|---|
| `POST /api/payments` | 60 requests/minute per IP | Payment submission; conservative to protect both the gateway and the bank simulator |
| `GET /api/payments/{id}` | 300 requests/minute per IP | Read-only retrieval; higher limit reflects the lower cost of the operation |

### Bank Call Timeout

The `HttpClient` used by `BankService` must have an explicit timeout. The default (100 seconds) is far too long for a payment flow. Configure it when registering the typed client:

```csharp
builder.Services.AddHttpClient<IBankService, BankService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BankSimulator:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

A `TaskCanceledException` thrown on timeout must be caught in `BankService` and treated as bank unavailability — return `502 Bad Gateway`, identical to the 503 path.

### No Retry on Bank Calls

`BankService` must not retry failed or timed-out bank requests. A payment request forwarded to the bank may have been received and processed even if the response was lost or the connection timed out. Retrying would risk double-charging the customer. If the bank is unreachable, return `502` immediately and let the merchant decide whether to resubmit.

---

## Observability

The gateway exposes structured logs via Serilog and Prometheus metrics via `prometheus-net`. Grafana visualises both, using Loki as the log data source and Prometheus as the metrics data source.

### Structured Logging (Serilog → Loki)

Use the `Serilog.AspNetCore` and `Serilog.Sinks.Grafana.Loki` packages. Configure the logger in `Program.cs` before building the host:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.GrafanaLoki(
        uri: "http://loki:3100",
        labels: [new LokiLabel { Key = "app", Value = "payment-gateway" }])
    .CreateLogger();

builder.Host.UseSerilog();
```

**PCI constraint:** Card numbers and CVV values must never appear in log messages. Only `last_four_digits` is safe to include.

#### Key log events

| Component | Event | Level | Structured fields |
|---|---|---|---|
| `PaymentsController` | Payment rejected (layer 2 validation failure) | `Information` | — |
| `BankService` | Bank request dispatched | `Information` | `currency`, `amount` |
| `BankService` | Bank response received | `Information` | `authorized` |
| `BankService` | Bank unreachable (503) | `Warning` | — |
| `PaymentsRepository` | Payment persisted | `Information` | `payment_id`, `status` |

Use structured log templates rather than string interpolation so that Loki can index fields as labels:

```csharp
_logger.LogInformation("Bank response received: {Authorized}", response.Authorized);
_logger.LogInformation("Payment persisted: {PaymentId} {Status}", payment.Id, payment.Status);
```

### Metrics (prometheus-net → Prometheus → Grafana)

Use the `prometheus-net.AspNetCore` package. In `Program.cs`:

```csharp
app.UseHttpMetrics(); // request count, duration, and in-progress per route — automatic
app.MapMetrics();     // exposes GET /metrics for Prometheus to scrape
```

Declare a custom counter as a static field in `BankService` to track bank call outcomes:

```csharp
private static readonly Counter BankRequests = Metrics.CreateCounter(
    "bank_requests_total",
    "Requests sent to the bank simulator",
    labelNames: ["outcome"]);
```

Increment it after each bank response:

```csharp
BankRequests.WithLabels("authorized").Inc();
BankRequests.WithLabels("declined").Inc();
BankRequests.WithLabels("unreachable").Inc();
```

#### Metrics exposed

| Metric | Type | Labels | Source |
|---|---|---|---|
| `http_requests_received_total` | Counter | `method`, `route`, `code` | `UseHttpMetrics()` |
| `http_request_duration_seconds` | Histogram | `method`, `route`, `code` | `UseHttpMetrics()` |
| `bank_requests_total` | Counter | `outcome` | `BankService` |

### Grafana Dashboards

Dashboards and data sources are provisioned automatically — no manual setup is needed after `docker-compose up -d`.

```
grafana/
  provisioning/
    datasources/
      datasources.yaml   # Prometheus (default) and Loki data sources
    dashboards/
      dashboards.yaml    # points Grafana at the dashboards directory
      payment-gateway.json
prometheus/
  prometheus.yml         # scrapes host.docker.internal:5000/metrics
```

The **Payment Gateway** dashboard (`payment-gateway.json`) contains four panels:

| Panel | Data source | Description |
|---|---|---|
| API Request Rate | Prometheus | Requests/second per route and HTTP status code |
| Bank Request Outcomes | Prometheus | Bank calls/second broken down by outcome (`authorized`, `declined`, `unreachable`) |
| Response Status Distribution | Prometheus | Pie chart of HTTP status codes over the last 5 minutes |
| Application Logs | Loki | Live structured log stream, newest first |

Grafana is available at `http://localhost:3000`. Default credentials: `admin` / `admin`.

---

## Running Locally

```bash
docker-compose up -d
```

Starts all services:

| Service | URL |
|---|---|
| Bank simulator | `http://localhost:8080` |
| Mountebank admin | `http://localhost:2525` |
| Prometheus | `http://localhost:9090` |
| Loki | `http://localhost:3100` |
| Grafana | `http://localhost:3000` |

---

## Testing Requirements

### Layer 1 — Schema Validation Tests

Layer 1 is enforced by the ASP.NET pipeline, so these must be integration tests using `WebApplicationFactory<T>`.

**`card_number`**
- Missing → 400
- Length < 14 → 400
- Length > 19 → 400
- Length 14 → passes layer 1
- Length 19 → passes layer 1

**`expiry_month`**
- Missing → 400
- 0 → 400
- 13 → 400
- 1 → passes layer 1
- 12 → passes layer 1

**`expiry_year`**
- Missing → 400

**`currency`**
- Missing → 400
- Unknown value (e.g. `"JPY"`) → 400
- `"USD"` → passes layer 1
- `"GBP"` → passes layer 1
- `"EUR"` → passes layer 1

**`amount`**
- Missing → 400
- 0 → 400
- Negative → 400
- 1 → passes layer 1

**`cvv`**
- Missing → 400
- Empty string → 400

**Unknown fields**
- Request body containing an unrecognised field → 400

### Layer 2 — Domain Validation Tests

Layer 2 is implemented in `PostPaymentRequestValidator`, which should be unit tested directly without the HTTP pipeline.

**`card_number`**
- Contains non-digit characters → invalid
- Fails Luhn check → invalid
- Passes Luhn check → valid

**`cvv`**
- Length 2 → invalid
- Length 5 → invalid
- Contains non-digit characters → invalid
- Length 3, digits only → valid
- Length 4, digits only → valid

**`expiry_year` + `expiry_month`**
- Month and year equal to current month and year → valid
- Month and year one month in the future → valid
- Month and year one month in the past → invalid

### End-to-End Tests

Integration tests via `WebApplicationFactory<T>` covering the full request lifecycle. The bank simulator must not need to be running — substitute `IBankService` with a mock via `WithWebHostBuilder`:

```csharp
webApplicationFactory.WithWebHostBuilder(builder =>
    builder.ConfigureServices(services =>
    {
        var mockBank = new Mock<IBankService>();
        mockBank.Setup(...).ReturnsAsync(...);
        services.AddSingleton(mockBank.Object);
    }));
```

**Authorized payment**
- Mock `IBankService` to return `authorized: true`
- POST valid payment request → 200 with status `Authorized`

**Declined payment**
- Mock `IBankService` to return `authorized: false`
- POST valid payment request → 200 with status `Declined`

**Rejected payment**
- No mock needed (bank is never called for an invalid request)
- POST request that fails layer 2 validation → 400 with status `Rejected`

**Retrieval**
- GET `/api/payments/{id}` with a known ID (seeded into `PaymentsRepository`) → 200
- GET `/api/payments/{id}` with an unknown ID → 404

**Idempotency**
- Missing `Idempotency-Key` header → 400
- Two sequential POST requests with the same key → second returns 200 with the same response body; bank called exactly once (verify via mock call count)
- POST with a key whose processing has been cancelled (e.g. validation failure) → subsequent POST with same key proceeds normally to the bank
- Concurrent POST requests with the same key → one returns 200, the other returns 409; bank called exactly once

### Integration Tests (Real Bank Simulator)

These tests exercise the full request lifecycle including real bank calls. Run `docker-compose up -d` before executing this suite. Place them in a separate test class marked `[Trait("Category", "Integration")]` so they are excluded from unit test runs where the simulator is not available.

#### Test card numbers

All three cards below pass the Luhn check and are 16 digits, satisfying layer 1 and 2 validation.

| Card number | Last digit | Bank response | Gateway outcome |
|---|---|---|---|
| `2222405343248877` | 7 | `200 authorized: true` | `Authorized` |
| `4242424242424242` | 2 | `200 authorized: false` | `Declined` |
| `4111111111111110` | 0 | `503 Service Unavailable` | `502 Bad Gateway` |

#### Test setup

Each test registers a fresh `PaymentsRepository` instance so no state leaks between tests:

```csharp
private (HttpClient client, WebApplicationFactory<Program> factory) CreateFreshClient()
{
    var factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IPaymentsRepository>(_ => new PaymentsRepository())));
    return (factory.CreateClient(), factory);
}
```

All requests must include a fresh `Idempotency-Key` header (`Guid.NewGuid().ToString()`) unless the test is specifically exercising key reuse.

---

**Authorized payment — full lifecycle**

- POST with card `2222405343248877`, CVV `123`, expiry in the future
- Assert: `200`, `status = "Authorized"`, `last_four_digits = "8877"`, `id` is a non-empty GUID, `currency`/`amount`/`expiry_month`/`expiry_year` match the request
- GET `/api/payments/{id}` with the returned ID
- Assert: `200`, every field matches the POST response exactly

---

**Declined payment — full lifecycle**

- POST with card `4242424242424242`, CVV `123`, expiry in the future
- Assert: `200`, `status = "Declined"`, `last_four_digits = "4242"`, `id` is a non-empty GUID
- GET `/api/payments/{id}` with the returned ID
- Assert: `200`, every field matches the POST response exactly

---

**Bank unavailable — nothing stored**

- POST with card `4111111111111110`, CVV `123`, expiry in the future
- Assert: `502`, response body is empty
- Access repository via `factory.Services.GetRequiredService<IPaymentsRepository>()` and assert it contains no completed entries

---

**Unknown payment ID — 404**

- POST an authorized payment and capture its ID
- GET `/api/payments/{random-guid}` where the GUID differs from the captured ID
- Assert: `404`

---

**Multiple sequential payments — independent storage**

- POST payment A (card `2222405343248877`, unique key) → assert `200 Authorized`, capture `id-A`
- POST payment B (card `4242424242424242`, unique key) → assert `200 Declined`, capture `id-B`
- Assert: `id-A ≠ id-B`
- GET `/api/payments/{id-A}` → assert `200`, `status = "Authorized"`
- GET `/api/payments/{id-B}` → assert `200`, `status = "Declined"`

---

**Idempotent retry — completed key returns cached response**

- POST with card `2222405343248877`, Idempotency-Key: `key-K` → assert `200 Authorized`, capture response body R1
- POST identical request again with Idempotency-Key: `key-K` → assert `200`, capture response body R2
- Assert: R1 and R2 are identical — same `id`, `status`, `last_four_digits`, `amount`, `currency`, `expiry_month`, `expiry_year`
- GET `/api/payments/{id}` → assert `200`, fields match R1

---

**Key released after rejection — retry with corrected request proceeds**

- POST with card `2222405343248877`, CVV `ab` (fails layer 2), Idempotency-Key: `key-K` → assert `400`, `status = "Rejected"`
- POST again with card `2222405343248877`, CVV `123` (valid), Idempotency-Key: `key-K` → assert `200 Authorized`, capture ID
- GET `/api/payments/{id}` → assert `200 Authorized`

---

**Concurrent payments — distinct keys, all succeed**

- Fire 10 concurrent POSTs, each with a unique idempotency key, all using card `2222405343248877`
- Assert: all 10 responses are `200 Authorized`
- Assert: all 10 returned IDs are distinct
- GET each of the 10 IDs → all return `200 Authorized`

---

**Concurrent duplicate key — exactly one payment stored**

- Fire 10 concurrent POSTs, all with the same Idempotency-Key, card `2222405343248877`
- Assert: every response is either `200` or `409` (no `4xx` other than `409`, no `5xx`)
- Assert: all `200` responses have identical bodies (same `id` and all other fields)
- Assert: at least one response is `200`
- GET the `id` from any `200` response → assert `200 Authorized`
- Access repository via `factory.Services` and assert exactly one completed entry exists for the key

---

## Dependencies

| Package | Purpose |
|---|---|
| `Swashbuckle.AspNetCore` | Swagger/OpenAPI docs (already referenced) |
| `LuhnDotNet` | Luhn algorithm validation for card numbers |
| `Serilog.AspNetCore` | Serilog integration with ASP.NET Core host and request logging |
| `Serilog.Sinks.Grafana.Loki` | Pushes structured log events to Loki |
| `prometheus-net.AspNetCore` | Prometheus metrics middleware and `/metrics` endpoint |
| `xunit` | Test framework (already scaffolded) |
| `Microsoft.AspNetCore.Mvc.Testing` | `WebApplicationFactory` for integration tests |
| `Moq` | Mocking `IBankService` in end-to-end tests |

No additional NuGet packages are strictly required. A validation library (e.g. `FluentValidation`) may be added if desired.
