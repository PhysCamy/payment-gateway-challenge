using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Unit;

public class PaymentsRepositoryTests
{
    private readonly PaymentsRepository _repository = new();

    private static PostPaymentResponse APayment(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Status = PaymentStatus.Authorized,
        LastFourDigits = "8877",
        ExpiryMonth = 4,
        ExpiryYear = 2030,
        Currency = Currency.GBP,
        Amount = 100
    };

    [Fact]
    public void TryBeginProcessing_ClaimsAnUnseenKey()
    {
        Assert.True(_repository.TryBeginProcessing("key"));
    }

    [Fact]
    public void TryBeginProcessing_RejectsAKeyAlreadyInFlight()
    {
        _repository.TryBeginProcessing("key");

        Assert.False(_repository.TryBeginProcessing("key"));
    }

    [Fact]
    public void TryBeginProcessing_RejectsAKeyThatHasCompleted()
    {
        _repository.TryBeginProcessing("key");
        _repository.Add(APayment(), "key");

        Assert.False(_repository.TryBeginProcessing("key"));
    }

    [Fact]
    public void Add_MakesThePaymentRetrievableById()
    {
        var payment = APayment();

        _repository.Add(payment, "key");

        Assert.Same(payment, _repository.Get(payment.Id));
    }

    [Fact]
    public void Add_CachesThePaymentAgainstItsIdempotencyKey()
    {
        var payment = APayment();

        _repository.Add(payment, "key");

        Assert.Same(payment, _repository.GetByIdempotencyKey("key"));
    }

    [Fact]
    public void CancelProcessing_ReleasesTheKeySoItCanBeClaimedAgain()
    {
        _repository.TryBeginProcessing("key");

        _repository.CancelProcessing("key");

        Assert.True(_repository.TryBeginProcessing("key"));
    }

    [Fact]
    public void CancelProcessing_DoesNotStoreAnything()
    {
        _repository.TryBeginProcessing("key");

        _repository.CancelProcessing("key");

        Assert.Null(_repository.GetByIdempotencyKey("key"));
    }

    [Fact]
    public void Get_ReturnsNullForAnUnknownId()
    {
        Assert.Null(_repository.Get(Guid.NewGuid()));
    }

    [Fact]
    public void GetByIdempotencyKey_ReturnsNullForAnUnknownKey()
    {
        Assert.Null(_repository.GetByIdempotencyKey("never-seen"));
    }

    [Fact]
    public async Task TryBeginProcessing_LetsExactlyOneConcurrentCallerClaimTheSameKey()
    {
        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ =>
                Task.Run(() => _repository.TryBeginProcessing("key"))));

        Assert.Equal(1, attempts.Count(claimed => claimed));
    }
}
