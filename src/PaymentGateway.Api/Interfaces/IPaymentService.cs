using System.Net;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Interfaces;

public interface IPaymentService
{
    Task<PaymentProcessingResult> ProcessPaymentAsync(PostPaymentRequest request);
    PostPaymentResponse? GetPayment(Guid id);
}

public interface IBankClient
{
    Task<HttpResponseMessage> SubmitPaymentAsync(object payload);
}
