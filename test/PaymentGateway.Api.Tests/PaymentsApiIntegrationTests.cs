using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentsApiIntegrationTests
{
    [Fact]
    public async Task CreatesPaymentAndRetrievesItThroughLocationHeader()
    {
        using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();
        var request = new
        {
            cardNumber = "4111111111111111",
            expiryMonth = 12,
            expiryYear = DateTime.UtcNow.Year + 1,
            currency = "GBP",
            amount = 1050,
            cvv = "012"
        };

        using var createResponse = await client.PostAsJsonAsync("/api/payments", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);

        using var createJson = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var paymentId = createJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Authorized", createJson.RootElement.GetProperty("status").GetString());
        Assert.EndsWith(
            $"/api/payments/{paymentId}",
            createResponse.Headers.Location!.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);

        using var getResponse = await client.GetAsync(createResponse.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getJson = await JsonDocument.ParseAsync(await getResponse.Content.ReadAsStreamAsync());
        Assert.Equal(paymentId, getJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Authorized", getJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("1111", getJson.RootElement.GetProperty("cardNumberLastFour").GetString());
    }

    [Fact]
    public async Task ReturnsBadRequestForMalformedJson()
    {
        using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/payments", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class PaymentApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBankClient>();
                services.AddSingleton<IBankClient, AuthorizedBankClient>();
            });
        }
    }

    private sealed class AuthorizedBankClient : IBankClient
    {
        public Task<HttpResponseMessage> SubmitPaymentAsync(object payload)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { authorized = true })
            });
        }
    }
}