using System.Net;

namespace PaymentGateway.Api.Tests.Unit;

/// <summary>
/// Verifies the Prometheus scrape endpoint wired up by <c>UseHttpMetrics()</c> and
/// <c>MapMetrics()</c> is exposed and records per-request metrics. The custom
/// <c>bank_requests_total</c> counter only registers once the real <c>BankService</c> is
/// exercised, so it is covered by the real-bank integration suite rather than here.
/// </summary>
public class ObservabilityTests
{
    [Fact]
    public async Task MetricsEndpoint_ExposesHttpRequestMetrics()
    {
        var client = new PaymentGatewayApplicationFactory().CreateClient();

        // Drive one routed request so UseHttpMetrics has something to count.
        await client.GetAsync($"/api/payments/{Guid.NewGuid()}");

        var response = await client.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http_requests_received_total", body);
    }
}
