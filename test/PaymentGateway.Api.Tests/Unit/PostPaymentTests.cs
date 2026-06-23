using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Unit;

/// <summary>
/// Component coverage of the <c>POST /api/payments</c> flow driven through the full ASP.NET
/// pipeline, with <see cref="IBankService"/> substituted by a mock so the suite runs without
/// the real simulator (hence it is not in the <c>Integration</c> category). Exercises the
/// controller wiring the bank simulator cannot drive deterministically: the bank outcome →
/// gateway status mapping, the <c>502</c> on bank unavailability, required-header enforcement,
/// and the idempotency-key lifecycle (cached replay, in-flight conflict, and release after a
/// rejection). The real-bank equivalents live in <c>Integration/PaymentLifecycleIntegrationTests</c>.
/// </summary>
public class PostPaymentTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new() { Converters = { new JsonStringEnumConverter() } };

    private const string AuthorizedCard = "2222405343248877"; // last four 8877
    private const string DeclinedCard = "4242424242424242";   // last four 4242

    private static Dictionary<string, object?> ValidPayload(
        string cardNumber = AuthorizedCard, string cvv = "123") => new()
    {
        ["card_number"] = cardNumber,
        ["expiry_month"] = 4,
        ["expiry_year"] = DateTime.UtcNow.Year + 1,
        ["currency"] = "GBP",
        ["amount"] = 100,
        ["cvv"] = cvv
    };

    private static Mock<IBankService> Bank(bool authorized)
    {
        var bank = new Mock<IBankService>();
        bank.Setup(b => b.ProcessPaymentAsync(It.IsAny<BankSimulatorRequest>()))
            .ReturnsAsync(new BankSimulatorResponse
            {
                Authorized = authorized,
                AuthorizationCode = Guid.NewGuid().ToString()
            });
        return bank;
    }

    private static Mock<IBankService> UnavailableBank()
    {
        var bank = new Mock<IBankService>();
        bank.Setup(b => b.ProcessPaymentAsync(It.IsAny<BankSimulatorRequest>()))
            .ThrowsAsync(new BankUnavailableException("The bank simulator is unavailable."));
        return bank;
    }

    private static HttpClient ClientFor(Mock<IBankService> bank) =>
        new PaymentGatewayApplicationFactory(bankService: bank.Object).CreateClient();

    private static HttpRequestMessage Post(object payload, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(payload)
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    [Fact]
    public async Task AuthorizedPayment_Returns200WithAuthorizedStatusAndMaskedCard()
    {
        var client = ClientFor(Bank(authorized: true));

        var response = await client.SendAsync(Post(ValidPayload(), Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
        Assert.Equal("8877", payment.LastFourDigits);
        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(Currency.GBP, payment.Currency);
        Assert.Equal(100, payment.Amount);
        Assert.Equal(4, payment.ExpiryMonth);
        Assert.Equal(DateTime.UtcNow.Year + 1, payment.ExpiryYear);
    }

    [Fact]
    public async Task DeclinedPayment_Returns200WithDeclinedStatus()
    {
        var client = ClientFor(Bank(authorized: false));

        var response = await client.SendAsync(
            Post(ValidPayload(DeclinedCard), Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Declined, payment!.Status);
        Assert.Equal("4242", payment.LastFourDigits);
    }

    [Fact]
    public async Task PostThenGet_ReturnsTheSamePersistedPayment()
    {
        var client = ClientFor(Bank(authorized: true));

        var postResponse = await client.SendAsync(Post(ValidPayload(), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var posted = await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
        Assert.NotNull(posted);

        var getResponse = await client.GetAsync($"/api/payments/{posted!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);

        Assert.NotNull(fetched);
        Assert.Equal(posted.Id, fetched!.Id);
        Assert.Equal(posted.Status, fetched.Status);
        Assert.Equal(posted.LastFourDigits, fetched.LastFourDigits);
        Assert.Equal(posted.ExpiryMonth, fetched.ExpiryMonth);
        Assert.Equal(posted.ExpiryYear, fetched.ExpiryYear);
        Assert.Equal(posted.Currency, fetched.Currency);
        Assert.Equal(posted.Amount, fetched.Amount);
    }

    [Fact]
    public async Task BankUnavailable_Returns502AndStoresNothing()
    {
        var bank = UnavailableBank();
        var factory = new PaymentGatewayApplicationFactory(bankService: bank.Object);
        var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString();

        var response = await client.SendAsync(Post(ValidPayload(), key));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        // The bank was never reached, so there is no terminal result cached against the key.
        var repository = factory.Services.GetRequiredService<IPaymentsRepository>();
        Assert.Null(repository.GetByIdempotencyKey(key));

        // And the key was released, so a retry is free to claim it again rather than 409-ing.
        bank.Reset();
        bank.Setup(b => b.ProcessPaymentAsync(It.IsAny<BankSimulatorRequest>()))
            .ReturnsAsync(new BankSimulatorResponse { Authorized = true });

        var retry = await client.SendAsync(Post(ValidPayload(), key));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task MissingIdempotencyKey_Returns400()
    {
        var client = ClientFor(Bank(authorized: true));

        var response = await client.SendAsync(Post(ValidPayload(), idempotencyKey: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RepeatedKey_ReplaysCachedResponseAndCallsBankOnce()
    {
        var bank = Bank(authorized: true);
        var client = ClientFor(bank);
        var key = Guid.NewGuid().ToString();

        var first = await client.SendAsync(Post(ValidPayload(), key));
        var firstPayment = await first.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);

        var second = await client.SendAsync(Post(ValidPayload(), key));
        var secondPayment = await second.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstPayment!.Id, secondPayment!.Id);
        Assert.Equal(firstPayment.Status, secondPayment.Status);
        Assert.Equal(firstPayment.LastFourDigits, secondPayment.LastFourDigits);

        bank.Verify(
            b => b.ProcessPaymentAsync(It.IsAny<BankSimulatorRequest>()), Times.Once);
    }

    [Fact]
    public async Task KeyReleasedAfterRejection_RetryWithCorrectedRequestProceeds()
    {
        var client = ClientFor(Bank(authorized: true));
        var key = Guid.NewGuid().ToString();

        var rejected = await client.SendAsync(Post(ValidPayload(cvv: "ab"), key));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var retried = await client.SendAsync(Post(ValidPayload(cvv: "123"), key));
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);

        var payment = await retried.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameKey_StoreExactlyOnePaymentAndCallBankOnce()
    {
        var bank = Bank(authorized: true);
        var client = ClientFor(bank);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => client.SendAsync(Post(ValidPayload(), key))));

        Assert.All(responses, r =>
            Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                $"Unexpected status {(int)r.StatusCode}."));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);

        var bodies = await Task.WhenAll(responses
            .Where(r => r.StatusCode == HttpStatusCode.OK)
            .Select(r => r.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions)));

        var distinctIds = bodies.Select(b => b!.Id).Distinct();
        Assert.Single(distinctIds);

        bank.Verify(
            b => b.ProcessPaymentAsync(It.IsAny<BankSimulatorRequest>()), Times.Once);
    }
}
