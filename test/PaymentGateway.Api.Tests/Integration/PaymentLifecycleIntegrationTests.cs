using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Integration;

/// <summary>
/// Full-pipeline lifecycle tests that exercise <c>PaymentsController</c> against the REAL
/// Mountebank bank simulator on <c>http://localhost:8080</c> — <see cref="PaymentGateway.Api.Interfaces.IBankService"/>
/// is not mocked. Run <c>docker-compose up -d</c> before this suite. Marked
/// <c>Category=Integration</c> so it is excluded from the default unit run
/// (<c>dotnet test --filter "Category!=Integration"</c>). Each test gets a fresh
/// <see cref="PaymentsRepository"/> so no state leaks between tests, and every POST carries a
/// unique <c>Idempotency-Key</c>.
/// </summary>
[Trait("Category", "Integration")]
public class PaymentLifecycleIntegrationTests
{
    private const string AuthorizedCard = "2222405343248877"; // last digit 7 → authorized
    private const string DeclinedCard = "4242424242424242";   // last digit 2 → declined
    // Last digit 0 → simulator 503 → gateway 502. Unlike the SPEC's 4111111111111110, this
    // number is Luhn-valid, so it clears the controller's Layer 2 check and actually reaches
    // the bank (4111111111111110 fails Luhn and would be rejected with 400 before the call).
    private const string UnreachableCard = "4242424242424200";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { Converters = { new JsonStringEnumConverter() } };

    private readonly PaymentsRepository _repository = new(NullLogger<PaymentsRepository>.Instance);
    private readonly HttpClient _client;

    public PaymentLifecycleIntegrationTests()
    {
        _client = new PaymentGatewayApplicationFactory(paymentsRepository: _repository).CreateClient();
    }

    private static Dictionary<string, object?> Payload(string cardNumber) => new()
    {
        ["card_number"] = cardNumber,
        ["expiry_month"] = 4,
        ["expiry_year"] = DateTime.UtcNow.Year + 1,
        ["currency"] = "GBP",
        ["amount"] = 100,
        ["cvv"] = "123"
    };

    private static HttpRequestMessage Post(object payload, string? idempotencyKey = null) =>
        new(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(payload),
            Headers = { { "Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString() } }
        };

    [Fact]
    public async Task AuthorizedPayment_FullLifecycle()
    {
        var post = await _client.SendAsync(Post(Payload(AuthorizedCard)));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var posted = await post.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
        Assert.NotNull(posted);
        Assert.Equal(PaymentStatus.Authorized, posted!.Status);
        Assert.Equal("8877", posted.LastFourDigits);
        Assert.NotEqual(Guid.Empty, posted.Id);
        Assert.Equal(Currency.GBP, posted.Currency);
        Assert.Equal(100, posted.Amount);

        var get = await _client.GetAsync($"/api/payments/{posted.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var fetched = await get.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
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
    public async Task DeclinedPayment_FullLifecycle()
    {
        var post = await _client.SendAsync(Post(Payload(DeclinedCard)));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var posted = await post.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
        Assert.NotNull(posted);
        Assert.Equal(PaymentStatus.Declined, posted!.Status);
        Assert.Equal("4242", posted.LastFourDigits);

        var get = await _client.GetAsync($"/api/payments/{posted.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var fetched = await get.Content.ReadFromJsonAsync<PostPaymentResponse>(JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(posted.Id, fetched!.Id);
        Assert.Equal(PaymentStatus.Declined, fetched.Status);
    }

    [Fact]
    public async Task BankUnavailable_Returns502AndStoresNothing()
    {
        var key = Guid.NewGuid().ToString();

        var response = await _client.SendAsync(Post(Payload(UnreachableCard), key));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Null(_repository.GetByIdempotencyKey(key));
    }

    [Fact]
    public async Task UnknownPaymentId_Returns404()
    {
        var response = await _client.GetAsync($"/api/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
