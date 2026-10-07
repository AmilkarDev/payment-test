using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(IPaymentService paymentService, ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<GetPaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentService.GetPayment(id);

        if (payment is null)
        {
            _logger.LogInformation("Payment {PaymentId} was not found", id);
            return NotFound();
        }

        return Ok(new GetPaymentResponse
        {
            Id = payment.Id,
            Status = payment.Status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount,
        });
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> CreatePaymentAsync([FromBody] PostPaymentRequest request)
    {
        if (request is null)
        {
            _logger.LogWarning("Payment creation request body was empty");
            return BadRequest();
        }

        try
        {
            var result = await _paymentService.ProcessPaymentAsync(request);
            if (result.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogWarning("Idempotency key was reused with different payment details");
                return Conflict(new ProblemDetails
                {
                    Title = "Idempotency key conflict",
                    Detail = "Use a new idempotency key for a different payment request."
                });
            }

            if (result.Payment is null)
            {
                _logger.LogWarning("Bank payment request ended with HTTP status {HttpStatusCode}", (int)result.StatusCode);
                return StatusCode((int)result.StatusCode);
            }

            _logger.LogInformation(
                "Payment {PaymentId} processed with status {PaymentStatus} and HTTP status {HttpStatusCode}",
                result.Payment.Id,
                result.Payment.Status,
                (int)result.StatusCode);

            if (result.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return Ok(result.Payment);
            }

            return CreatedAtAction(nameof(GetPayment), new { id = result.Payment.Id }, result.Payment);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Payment creation request failed validation");
            return BadRequest(new PaymentRejectionResponse(PaymentStatus.Rejected, ex.Message));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Bank dependency failed");
            return StatusCode(StatusCodes.Status502BadGateway, new PaymentRejectionResponse(
                PaymentStatus.Rejected,
                "Bank service unavailable"));
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Bank dependency timed out");
            return StatusCode(StatusCodes.Status504GatewayTimeout, new PaymentRejectionResponse(
                PaymentStatus.Rejected,
                "Bank request timed out"));
        }
    }
}