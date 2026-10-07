using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Models.Requests;

public class PostPaymentRequest
{
    public const int MaxIdempotencyKeyLength = 128;

    public string? CardNumber { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Currency { get; set; }
    public int? Amount { get; set; }
    public string? Cvv { get; set; }

    [StringLength(MaxIdempotencyKeyLength, ErrorMessage = "Idempotency key must be 128 characters or fewer.")]
    public string? IdempotencyKey { get; set; }
}