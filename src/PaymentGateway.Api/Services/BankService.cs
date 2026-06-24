using System.Net;
using System.Net.Http.Json;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

using Prometheus;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Sole implementation of <see cref="IBankService"/>, registered as a typed
/// <see cref="HttpClient"/>. Holds no mutable instance state, so it is safe to use
/// concurrently. Bank calls are never retried: a forwarded request may have been processed
/// even if the response was lost, so retrying would risk double-charging the customer.
/// </summary>
public sealed class BankService : IBankService
{
    // Counts every call to the bank simulator, labelled by its outcome so Grafana can chart
    // the authorized/declined/unreachable mix. Static: one series shared across all instances.
    private static readonly Counter BankRequests = Metrics.CreateCounter(
        "bank_requests_total",
        "Requests sent to the bank simulator",
        labelNames: ["outcome"]);

    private readonly HttpClient _httpClient;
    private readonly ILogger<BankService> _logger;

    public BankService(HttpClient httpClient, ILogger<BankService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BankSimulatorResponse> ProcessPaymentAsync(BankSimulatorRequest request)
    {
        // PCI: log only non-sensitive fields — never the card number or CVV.
        _logger.LogInformation(
            "Bank request dispatched: {Currency} {Amount}", request.Currency, request.Amount);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/payments", request);
        }
        catch (TaskCanceledException ex)
        {
            // The configured client timeout surfaces as TaskCanceledException; treat it
            // identically to the bank being unreachable.
            _logger.LogWarning("Bank unreachable: request timed out.");
            BankRequests.WithLabels("unreachable").Inc();
            throw new BankUnavailableException("The bank simulator did not respond in time.", ex);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogWarning("Bank unreachable: simulator returned 503.");
            BankRequests.WithLabels("unreachable").Inc();
            throw new BankUnavailableException("The bank simulator is unavailable.");
        }

        response.EnsureSuccessStatusCode();

        var decision = await response.Content.ReadFromJsonAsync<BankSimulatorResponse>()
            ?? throw new BankUnavailableException("The bank simulator returned an empty response.");

        _logger.LogInformation("Bank response received: {Authorized}", decision.Authorized);
        BankRequests.WithLabels(decision.Authorized ? "authorized" : "declined").Inc();

        return decision;
    }
}
