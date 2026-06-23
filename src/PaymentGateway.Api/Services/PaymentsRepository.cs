using System.Collections.Concurrent;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Sole implementation of <see cref="IPaymentsRepository"/>. Backed by
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> instances, so all operations are atomic
/// and no external locking is required.
/// </summary>
public sealed class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();
    private readonly ConcurrentDictionary<string, PostPaymentResponse> _completed = new();
    // Used as a concurrent set: .NET has no ConcurrentHashSet, so a ConcurrentDictionary
    // with a throwaway value is the canonical idiom. Only the keys carry meaning.
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();
    private readonly ILogger<PaymentsRepository> _logger;

    public PaymentsRepository(ILogger<PaymentsRepository> logger)
    {
        _logger = logger;
    }

    public bool TryBeginProcessing(string idempotencyKey)
    {
        if (_completed.ContainsKey(idempotencyKey))
        {
            return false;
        }

        // TryAdd is atomic: only one concurrent caller can claim an unclaimed key.
        return _inFlight.TryAdd(idempotencyKey, 0);
    }

    public void Add(PostPaymentResponse payment, string idempotencyKey)
    {
        _payments[payment.Id] = payment;
        _completed[idempotencyKey] = payment;
        _inFlight.TryRemove(idempotencyKey, out _);

        // last_four_digits only — never the full PAN. PostPaymentResponse holds no PAN by design.
        _logger.LogInformation(
            "Payment persisted: {PaymentId} {Status}", payment.Id, payment.Status);
    }

    public void CancelProcessing(string idempotencyKey)
    {
        _inFlight.TryRemove(idempotencyKey, out _);
    }

    public PostPaymentResponse? Get(Guid id)
    {
        return _payments.TryGetValue(id, out var payment) ? payment : null;
    }

    public PostPaymentResponse? GetByIdempotencyKey(string idempotencyKey)
    {
        return _completed.TryGetValue(idempotencyKey, out var payment) ? payment : null;
    }
}
