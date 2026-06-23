using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Interfaces;

namespace PaymentGateway.Api.Tests;

/// <summary>
/// Shared <see cref="WebApplicationFactory{T}"/> for the integration tests. Optionally
/// swaps in a pre-seeded <see cref="IPaymentsRepository"/> or a substitute
/// <see cref="IBankService"/> so a test can arrange the data a request reads back and
/// drive the bank's decision without the real simulator running.
/// </summary>
public class PaymentGatewayApplicationFactory : WebApplicationFactory<PaymentsController>
{
    private readonly IPaymentsRepository? _paymentsRepository;
    private readonly IBankService? _bankService;

    public PaymentGatewayApplicationFactory(
        IPaymentsRepository? paymentsRepository = null,
        IBankService? bankService = null)
    {
        _paymentsRepository = paymentsRepository;
        _bankService = bankService;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            if (_paymentsRepository is not null)
            {
                services.AddSingleton(_paymentsRepository);
            }

            if (_bankService is not null)
            {
                services.AddSingleton(_bankService);
            }
        });
    }
}
