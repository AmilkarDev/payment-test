using System.Diagnostics;
using System.Net.Http.Json;

using PaymentGateway.Api.Interfaces;

namespace PaymentGateway.Api.Services;

public class BankClient : IBankClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BankClient> _logger;

    public BankClient(HttpClient httpClient, ILogger<BankClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<HttpResponseMessage> SubmitPaymentAsync(object payload)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/payments", payload);
            _logger.LogInformation(
                "Bank payment request completed with HTTP status {HttpStatusCode} in {ElapsedMilliseconds} ms",
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Bank payment request failed after {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Bank payment request was canceled or timed out after {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
