using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IPaymentRequestValidator _validator;

    public PaymentsController(
        IPaymentsRepository paymentsRepository,
        IPaymentRequestValidator validator)
    {
        _paymentsRepository = paymentsRepository;
        _validator = validator;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PostPaymentResponse?>> GetPaymentAsync(Guid id)
    {
        var payment = _paymentsRepository.Get(id);

        if (payment is null)
        {
            return NotFound($"No payment found with ID '{id}'.");
        }

        return new OkObjectResult(payment);
    }

    // Layer 1 (model binding + data annotations) runs before this action: [ApiController]
    // returns 400 automatically when binding fails. Layer 2 (domain validation) runs here.
    // The bank call and persistence are implemented in later phases.
    [HttpPost]
    public ActionResult<PostPaymentResponse> PostPayment(PostPaymentRequest request)
    {
        var validation = _validator.Validate(request);
        if (!validation.IsValid)
        {
            return BadRequest(new RejectedPaymentResponse(validation.Errors));
        }

        return Ok();
    }
}