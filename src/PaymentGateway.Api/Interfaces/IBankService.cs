using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Interfaces;

/// <summary>
/// Contract for forwarding a validated payment to the acquiring bank simulator. The sole
/// implementation is registered as a typed <see cref="HttpClient"/> via
/// <c>IHttpClientFactory</c> and holds no mutable instance state.
/// </summary>
public interface IBankService
{
    /// <summary>
    /// Forwards a payment to the bank and returns its authorization decision.
    /// </summary>
    /// <param name="request">The bank-shaped payment request (expiry formatted as <c>MM/YYYY</c>).</param>
    /// <returns>The bank's authorization decision.</returns>
    /// <exception cref="BankUnavailableException">
    /// The bank was unreachable — it returned <c>503</c> or the request timed out. The
    /// payment must be treated as not processed (the gateway returns <c>502 Bad Gateway</c>).
    /// </exception>
    Task<BankSimulatorResponse> ProcessPaymentAsync(BankSimulatorRequest request);
}
