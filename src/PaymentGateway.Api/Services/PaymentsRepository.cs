using System.Collections.Concurrent;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public interface IPaymentsRepository
{
    void Add(PostPaymentResponse payment);
    PostPaymentResponse? Get(Guid id);
    List<PostPaymentResponse> GetAll();
    IdempotentPayment? GetByIdempotencyKey(string idempotencyKey);
    void SaveIdempotencyKey(string idempotencyKey, Guid paymentId, string requestFingerprint);
}

public sealed record IdempotentPayment(PostPaymentResponse Payment, string RequestFingerprint);

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _idempotencyKeys = new();

    public void Add(PostPaymentResponse payment)
    {
        _payments[payment.Id] = payment;
    }

    public PostPaymentResponse? Get(Guid id)
    {
        return _payments.TryGetValue(id, out var payment) ? payment : null;
    }

    public List<PostPaymentResponse> GetAll()
    {
        return _payments.Values.ToList();
    }

    public IdempotentPayment? GetByIdempotencyKey(string idempotencyKey)
    {
        if (!_idempotencyKeys.TryGetValue(idempotencyKey, out var entry))
        {
            return null;
        }

        var payment = Get(entry.PaymentId);
        return payment is null ? null : new IdempotentPayment(payment, entry.RequestFingerprint);
    }

    public void SaveIdempotencyKey(string idempotencyKey, Guid paymentId, string requestFingerprint)
    {
        _idempotencyKeys[idempotencyKey] = new IdempotencyEntry(paymentId, requestFingerprint);
    }

    private sealed record IdempotencyEntry(Guid PaymentId, string RequestFingerprint);
}