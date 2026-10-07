using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public static class PaymentRequestValidator
{
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.Ordinal)
    {
        "EUR",
        "GBP",
        "USD"
    };

    private const int MaxIdempotencyKeyLength = PostPaymentRequest.MaxIdempotencyKeyLength;

    public static void Validate(PostPaymentRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var cardNumber = request.CardNumber ?? string.Empty;
        var hasValidCardNumber = cardNumber.Length is >= 14 and <= 19
            && cardNumber.All(character => character is >= '0' and <= '9');

        var hasValidIdempotencyKey = request.IdempotencyKey is null
            || (request.IdempotencyKey.Trim().Length > 0
                && request.IdempotencyKey.Length <= MaxIdempotencyKeyLength);

        var hasValidExpiry = request.ExpiryMonth is >= 1 and <= 12
            && request.ExpiryYear is not null
            && IsExpiryInFuture(request.ExpiryMonth!.Value, request.ExpiryYear!.Value);

        var normalizedCurrency = request.Currency?.Trim();
        var hasValidCurrency = request.Currency is { Length: 3 }
            && normalizedCurrency == request.Currency
            && SupportedCurrencies.Contains(normalizedCurrency.ToUpperInvariant());

        var hasValidCvv = request.Cvv is { Length: >= 3 and <= 4 } cvv
            && cvv.All(character => character is >= '0' and <= '9');

        if (!hasValidCardNumber
            || !hasValidCurrency
            || request.Amount is null || request.Amount <= 0
            || !hasValidCvv
            || !hasValidExpiry
            || (request.IdempotencyKey is not null && !hasValidIdempotencyKey))
        {
            throw new InvalidOperationException(
                request.IdempotencyKey is not null && request.IdempotencyKey.Trim().Length == 0
                    ? "Idempotency key cannot be empty."
                    : request.IdempotencyKey is not null
                        ? $"Idempotency key must be {MaxIdempotencyKeyLength} characters or fewer."
                        : "Not all required properties were sent in the request");
        }
    }

    private static bool IsExpiryInFuture(int expiryMonth, int expiryYear)
    {
        var currentDate = DateTime.UtcNow;
        return expiryYear > currentDate.Year
            || (expiryYear == currentDate.Year && expiryMonth >= currentDate.Month);
    }
}
