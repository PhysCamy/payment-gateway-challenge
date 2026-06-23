using System.Text.Json.Serialization;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validators;

using Prometheus;

using Serilog;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: console for local dev, Loki for Grafana. The API runs on the host
// (not in docker-compose), so Loki is reached on localhost — overridable via config for
// other topologies. PCI: card numbers and CVVs must never be logged; only last_four_digits.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.GrafanaLoki(
        builder.Configuration["Observability:LokiUri"] ?? "http://localhost:3100",
        labels: [new LokiLabel { Key = "app", Value = "payment-gateway" }])
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new CurrencyJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Liveness probe for cloud load balancers: 200 if the process and pipeline are responsive.
builder.Services.AddHealthChecks();

builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddSingleton<IPaymentRequestValidator, PostPaymentRequestValidator>();

builder.Services.AddHttpClient<IBankService, BankService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BankSimulator:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Emit one structured log line per HTTP request (method, path, status, elapsed) to Loki.
app.UseSerilogRequestLogging();

// Record request count, duration, and in-progress gauge per route — exposed for Prometheus.
app.UseHttpMetrics();

app.UseAuthorization();

app.MapControllers();

// GET /health — liveness probe for cloud orchestrators. No auth, no dependency checks.
app.MapHealthChecks("/health");

// GET /metrics — scraped by Prometheus. No card data flows through here.
app.MapMetrics();

app.Run();
