using System.Net;
using System.Net.Http.Json;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Sole implementation of <see cref="IBankService"/>, registered as a typed
/// <see cref="HttpClient"/>. Holds no mutable instance state, so it is safe to use
/// concurrently. Bank calls are never retried: a forwarded request may have been processed
/// even if the response was lost, so retrying would risk double-charging the customer.
/// </summary>
public sealed class BankService : IBankService
{
    private readonly HttpClient _httpClient;

    public BankService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BankSimulatorResponse> ProcessPaymentAsync(BankSimulatorRequest request)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/payments", request);
        }
        catch (TaskCanceledException ex)
        {
            // The configured client timeout surfaces as TaskCanceledException; treat it
            // identically to the bank being unreachable.
            throw new BankUnavailableException("The bank simulator did not respond in time.", ex);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new BankUnavailableException("The bank simulator is unavailable.");
        }

        response.EnsureSuccessStatusCode();

        var decision = await response.Content.ReadFromJsonAsync<BankSimulatorResponse>();
        return decision
            ?? throw new BankUnavailableException("The bank simulator returned an empty response.");
    }
}
