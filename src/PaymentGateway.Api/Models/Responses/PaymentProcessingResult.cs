using System.Net;

namespace PaymentGateway.Api.Models.Responses;

public sealed class PaymentProcessingResult
{
    public PaymentProcessingResult(PostPaymentResponse? payment, HttpStatusCode statusCode)
    {
        Payment = payment;
        StatusCode = statusCode;
    }

    public PostPaymentResponse? Payment { get; }
    public HttpStatusCode StatusCode { get; }
}
