using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IPaymentRequestValidator _validator;
    private readonly IBankService _bankService;

    public PaymentsController(
        IPaymentsRepository paymentsRepository,
        IPaymentRequestValidator validator,
        IBankService bankService)
    {
        _paymentsRepository = paymentsRepository;
        _validator = validator;
        _bankService = bankService;
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
    // returns 400 automatically when binding fails — including when the required
    // Idempotency-Key header is absent. Everything below runs only once the request is
    // structurally valid and carries a key.
    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync(
        PostPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey)
    {
        var cached = _paymentsRepository.GetByIdempotencyKey(idempotencyKey);
        if (cached is not null)
        {
            return Ok(cached);
        }

        if (!_paymentsRepository.TryBeginProcessing(idempotencyKey))
        {
            // The claim failed because the key is in-flight, or it completed in the race
            // window since the check above. Return the cached result if it now exists,
            // otherwise the merchant is racing an in-flight request and should retry shortly.
            cached = _paymentsRepository.GetByIdempotencyKey(idempotencyKey);
            return cached is not null ? Ok(cached) : Conflict();
        }

        try
        {
            var validation = _validator.Validate(request);
            if (!validation.IsValid)
            {
                _paymentsRepository.CancelProcessing(idempotencyKey);
                return BadRequest(new RejectedPaymentResponse(validation.Errors));
            }

            PostPaymentResponse payment;
            try
            {
                var decision = await _bankService.ProcessPaymentAsync(ToBankRequest(request));
                payment = ToPaymentResponse(request, decision);
            }
            catch (BankUnavailableException)
            {
                // The bank was never reached, so the key carries no result — release it so
                // the merchant can retry with the same key. Return a bodiless 502: a bare
                // StatusCode(502) would be auto-mapped to a ProblemDetails body by
                // [ApiController], but there is nothing to report.
                _paymentsRepository.CancelProcessing(idempotencyKey);
                Response.StatusCode = StatusCodes.Status502BadGateway;
                return new EmptyResult();
            }

            _paymentsRepository.Add(payment, idempotencyKey);
            return Ok(payment);
        }
        catch
        {
            _paymentsRepository.CancelProcessing(idempotencyKey);
            throw;
        }
    }

    private static BankSimulatorRequest ToBankRequest(PostPaymentRequest request) => new()
    {
        CardNumber = request.CardNumber!,
        ExpiryDate = $"{request.ExpiryMonth!.Value:D2}/{request.ExpiryYear!.Value}",
        Currency = request.Currency!.Value.ToString(),
        Amount = request.Amount!.Value,
        Cvv = request.Cvv!
    };

    private static PostPaymentResponse ToPaymentResponse(
        PostPaymentRequest request, BankSimulatorResponse decision) => new()
    {
        Id = Guid.NewGuid(),
        Status = decision.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
        LastFourDigits = request.CardNumber![^4..],
        ExpiryMonth = request.ExpiryMonth!.Value,
        ExpiryYear = request.ExpiryYear!.Value,
        Currency = request.Currency!.Value,
        Amount = request.Amount!.Value
    };
}