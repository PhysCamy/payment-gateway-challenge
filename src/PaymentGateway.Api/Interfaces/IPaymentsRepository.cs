using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Interfaces;

/// <summary>
/// In-memory store for processed payments and the idempotency-key lifecycle. The sole
/// implementation is registered as a singleton and must be safe under concurrent access.
/// By design the stored type is <see cref="PostPaymentResponse"/> (which holds only
/// <c>last_four_digits</c>), so the full card number is never persisted.
/// </summary>
public interface IPaymentsRepository
{
    /// <summary>
    /// Atomically claims an idempotency key as in-flight.
    /// </summary>
    /// <param name="idempotencyKey">The merchant-supplied key for this payment attempt.</param>
    /// <returns>
    /// <c>true</c> if the key was unclaimed and is now in-flight; <c>false</c> if a request
    /// with the same key is already in-flight or has already completed.
    /// </returns>
    bool TryBeginProcessing(string idempotencyKey);

    /// <summary>
    /// Stores a terminal payment result and releases its in-flight claim. Only
    /// <c>Authorized</c> and <c>Declined</c> outcomes are persisted.
    /// </summary>
    /// <param name="payment">The payment to store, keyed internally by its ID.</param>
    /// <param name="idempotencyKey">The key the result is cached against for retries.</param>
    void Add(PostPaymentResponse payment, string idempotencyKey);

    /// <summary>
    /// Releases an in-flight claim without storing a result, so the key can be reused.
    /// Used when validation fails or the bank is unreachable.
    /// </summary>
    /// <param name="idempotencyKey">The key to release.</param>
    void CancelProcessing(string idempotencyKey);

    /// <summary>
    /// Retrieves a stored payment by its gateway-assigned ID.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <returns>The stored payment, or <c>null</c> if no payment exists for the ID.</returns>
    PostPaymentResponse? Get(Guid id);

    /// <summary>
    /// Retrieves the cached result for a completed idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">The key to look up.</param>
    /// <returns>The cached payment, or <c>null</c> if the key has not completed.</returns>
    PostPaymentResponse? GetByIdempotencyKey(string idempotencyKey);
}
