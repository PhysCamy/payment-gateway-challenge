using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Interfaces;

namespace PaymentGateway.Api.Tests.Integration;

/// <summary>
/// Shared <see cref="WebApplicationFactory{T}"/> for the integration tests. Sets the
/// <c>https_port</c> so HTTPS redirection resolves to a concrete URL under test, and
/// optionally swaps in a pre-seeded <see cref="IPaymentsRepository"/> so a test can
/// arrange the data a request will read back.
/// </summary>
public class PaymentGatewayApplicationFactory : WebApplicationFactory<PaymentsController>
{
    private readonly IPaymentsRepository? _paymentsRepository;

    public PaymentGatewayApplicationFactory(IPaymentsRepository? paymentsRepository = null)
    {
        _paymentsRepository = paymentsRepository;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("https_port", "443");

        if (_paymentsRepository is not null)
        {
            builder.ConfigureServices(services =>
                services.AddSingleton(_paymentsRepository));
        }
    }
}
