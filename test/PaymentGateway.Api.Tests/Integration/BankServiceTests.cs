using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Integration;

/// <summary>
/// Exercises <see cref="BankService"/> against the real Mountebank bank simulator on
/// <c>http://localhost:8080</c>. Run <c>docker-compose up -d</c> before this suite. Mocking
/// the <see cref="HttpClient"/> would only prove the stub returns what it was told to;
/// these tests verify the actual request shape and the simulator's real responses.
/// </summary>
[Trait("Category", "Integration")]
public class BankServiceTests
{
    private const string AuthorizedCard = "2222405343248877"; // ends 7 → authorized
    private const string DeclinedCard = "4242424242424242";   // ends 2 → declined
    private const string UnreachableCard = "4111111111111110"; // ends 0 → 503
    private const string SlowCard = "0000000000000000";        // simulator waits 2s before replying

    private static BankSimulatorRequest RequestFor(string cardNumber) => new()
    {
        CardNumber = cardNumber,
        ExpiryDate = "04/2030",
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    private static BankService BankService(TimeSpan? timeout = null)
    {
        var client = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
        if (timeout is not null)
        {
            client.Timeout = timeout.Value;
        }

        return new BankService(client, NullLogger<BankService>.Instance);
    }

    [Fact]
    public async Task ProcessPaymentAsync_AuthorizesAPaymentAndReturnsAnAuthorizationCode()
    {
        var decision = await BankService().ProcessPaymentAsync(RequestFor(AuthorizedCard));

        Assert.True(decision.Authorized);
        Assert.False(string.IsNullOrWhiteSpace(decision.AuthorizationCode));
    }

    [Fact]
    public async Task ProcessPaymentAsync_DeclinesAPayment()
    {
        var decision = await BankService().ProcessPaymentAsync(RequestFor(DeclinedCard));

        Assert.False(decision.Authorized);
    }

    [Fact]
    public async Task ProcessPaymentAsync_ThrowsBankUnavailableWhenTheBankReturns503()
    {
        await Assert.ThrowsAsync<BankUnavailableException>(
            () => BankService().ProcessPaymentAsync(RequestFor(UnreachableCard)));
    }

    [Fact]
    public async Task ProcessPaymentAsync_ThrowsBankUnavailableWhenTheCallTimesOut()
    {
        // The simulator deliberately holds this card's response for 2s (a server-side wait
        // behaviour), so a 500ms client timeout always fires first — deterministically,
        // not as a round-trip race. BankService must surface that as bank unavailability.
        var service = BankService(timeout: TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAsync<BankUnavailableException>(
            () => service.ProcessPaymentAsync(RequestFor(SlowCard)));
    }
}
