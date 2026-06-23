namespace PaymentGateway.Api.Services;

/// <summary>
/// Raised by <see cref="BankService"/> when the bank simulator cannot be reached — either
/// it returned <c>503 Service Unavailable</c> or the request timed out. Signals that the
/// payment was not processed, so the gateway should respond with <c>502 Bad Gateway</c>.
/// </summary>
public sealed class BankUnavailableException : Exception
{
    public BankUnavailableException(string message) : base(message)
    {
    }

    public BankUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
