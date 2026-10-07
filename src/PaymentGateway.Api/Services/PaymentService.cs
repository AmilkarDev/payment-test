using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentService : IPaymentService
{
    private static readonly byte[] IdempotencyFingerprintKey = RandomNumberGenerator.GetBytes(32);

    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IBankClient _bankClient;

    public PaymentService(IPaymentsRepository paymentsRepository, IBankClient bankClient)
    {
        _paymentsRepository = paymentsRepository;
        _bankClient = bankClient;
    }

    public async Task<PaymentProcessingResult> ProcessPaymentAsync(PostPaymentRequest request)
    {
        PaymentRequestValidator.Validate(request);

        var currency = request.Currency!.Trim().ToUpperInvariant();
        var requestFingerprint = CreateRequestFingerprint(request, currency);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingPayment = _paymentsRepository.GetByIdempotencyKey(request.IdempotencyKey);
            if (existingPayment is not null)
            {
                var storedFingerprint = Convert.FromHexString(existingPayment.RequestFingerprint);
                var incomingFingerprint = Convert.FromHexString(requestFingerprint);
                if (!CryptographicOperations.FixedTimeEquals(storedFingerprint, incomingFingerprint))
                {
                    return new PaymentProcessingResult(null, HttpStatusCode.Conflict);
                }

                return new PaymentProcessingResult(existingPayment.Payment, HttpStatusCode.OK);
            }
        }

        var expiryDate = $"{request.ExpiryMonth:D2}/{request.ExpiryYear}";

        using var response = await _bankClient.SubmitPaymentAsync(new
        {
            card_number = request.CardNumber,
            expiry_date = expiryDate,
            currency,
            amount = request.Amount,
            cvv = request.Cvv
        });

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var statusCode = response.StatusCode == HttpStatusCode.ServiceUnavailable
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.BadGateway;
            return new PaymentProcessingResult(null, statusCode);
        }

        BankAuthorizationResponse? responseBody;
        try
        {
            responseBody = await response.Content.ReadFromJsonAsync<BankAuthorizationResponse>();
        }
        catch (JsonException)
        {
            return new PaymentProcessingResult(null, HttpStatusCode.BadGateway);
        }

        if (responseBody?.Authorized is not bool authorized)
        {
            return new PaymentProcessingResult(null, HttpStatusCode.BadGateway);
        }

        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            CardNumberLastFour = request.CardNumber![^4..],
            ExpiryMonth = request.ExpiryMonth!.Value,
            ExpiryYear = request.ExpiryYear!.Value,
            Currency = currency,
            Amount = request.Amount ?? 0,
            Status = authorized ? PaymentStatus.Authorized : PaymentStatus.Declined
        };

        _paymentsRepository.Add(payment);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            _paymentsRepository.SaveIdempotencyKey(request.IdempotencyKey, payment.Id, requestFingerprint);
        }

        return new PaymentProcessingResult(payment, HttpStatusCode.Created);
    }

    public PostPaymentResponse? GetPayment(Guid id)
    {
        return _paymentsRepository.Get(id);
    }

    private static string CreateRequestFingerprint(PostPaymentRequest request, string currency)
    {
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            cardNumber = request.CardNumber,
            expiryMonth = request.ExpiryMonth,
            expiryYear = request.ExpiryYear,
            currency,
            amount = request.Amount
        });

        return Convert.ToHexString(HMACSHA256.HashData(IdempotencyFingerprintKey, requestBytes));
    }

    private sealed class BankAuthorizationResponse
    {
        public bool? Authorized { get; set; }
    }
}
