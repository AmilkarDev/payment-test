using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Models.Responses;

public sealed record PaymentRejectionResponse(PaymentStatus Status, string Message);