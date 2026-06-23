using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly PaymentsRepository _paymentsRepository;

    public PaymentsController(PaymentsRepository paymentsRepository)
    {
        _paymentsRepository = paymentsRepository;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PostPaymentResponse?>> GetPaymentAsync(Guid id)
    {
        var payment = _paymentsRepository.Get(id);

        return new OkObjectResult(payment);
    }

    // Layer 2 (domain validation), the bank call and persistence are implemented in
    // later phases. For now the action exists so Layer 1 (model binding + data
    // annotations) runs: [ApiController] returns 400 automatically when binding fails.
    [HttpPost]
    public ActionResult<PostPaymentResponse> PostPayment(PostPaymentRequest request)
    {
        return Ok();
    }
}