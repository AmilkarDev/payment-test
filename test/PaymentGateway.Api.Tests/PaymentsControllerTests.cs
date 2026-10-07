using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    [Fact]
    public void RetrievesAPaymentSuccessfully()
    {
        // Arrange
        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            ExpiryYear = 2027,
            ExpiryMonth = 12,
            Amount = 150,
            CardNumberLastFour = "1234",
            Currency = "GBP",
            Status = PaymentStatus.Authorized
        };

        var repository = new PaymentsRepository();
        repository.Add(payment);

        var controller = new PaymentsController(new PaymentService(repository, new StubBankClient()), NullLogger<PaymentsController>.Instance);

        // Act
        var result = controller.GetPayment(payment.Id);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetPaymentResponse>(okResult.Value);
        Assert.Equal(payment.Id, response.Id);
        Assert.Equal(PaymentStatus.Authorized, response.Status);
    }

    [Fact]
    public void Returns404IfPaymentNotFound()
    {
        // Arrange
        var controller = new PaymentsController(new PaymentService(new PaymentsRepository(), new StubBankClient()), NullLogger<PaymentsController>.Instance);

        // Act
        var result = controller.GetPayment(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreatesAuthorizedPaymentWhenBankApproves()
    {
        // Arrange
        var repository = new PaymentsRepository();
        var controller = new PaymentsController(new PaymentService(repository, new StubBankClient(
            new { authorized = true },
            HttpStatusCode.OK)), NullLogger<PaymentsController>.Instance);

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };

        // Act
        var result = await controller.CreatePaymentAsync(request);

        // Assert
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var payment = Assert.IsType<PostPaymentResponse>(created.Value);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("1111", payment.CardNumberLastFour);
        Assert.NotNull(repository.Get(payment.Id));
    }

    [Fact]
    public async Task PreservesLeadingZerosInReturnedLastFourDigits()
    {
        var controller = new PaymentsController(
            new PaymentService(new PaymentsRepository(), new StubBankClient()),
            NullLogger<PaymentsController>.Instance);
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111110001",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };

        var result = await controller.CreatePaymentAsync(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var payment = Assert.IsType<PostPaymentResponse>(created.Value);
        Assert.Equal("0001", payment.CardNumberLastFour);
    }

    [Fact]
    public async Task PreservesLeadingZeroCvvAsAStringWhenCallingTheBank()
    {
        var bankClient = new StubBankClient();
        var controller = new PaymentsController(
            new PaymentService(new PaymentsRepository(), bankClient),
            NullLogger<PaymentsController>.Instance);
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "012"
        };

        var result = await controller.CreatePaymentAsync(request);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal("012", JsonSerializer.SerializeToElement(bankClient.LastPayload).GetProperty("cvv").GetString());
    }

    [Theory]
    [InlineData(14)]
    [InlineData(19)]
    public async Task AcceptsCardNumbersAtSupportedLengthBoundaries(int length)
    {
        var controller = new PaymentsController(
            new PaymentService(new PaymentsRepository(), new StubBankClient()),
            NullLogger<PaymentsController>.Instance);
        var request = new PostPaymentRequest
        {
            CardNumber = new string('1', length),
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };

        var result = await controller.CreatePaymentAsync(request);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task CreatesDeclinedPaymentWhenBankDeclines()
    {
        // Arrange
        var repository = new PaymentsRepository();
        var controller = new PaymentsController(new PaymentService(repository, new StubBankClient(
            new { authorized = false },
            HttpStatusCode.OK)), NullLogger<PaymentsController>.Instance);

        var request = new PostPaymentRequest
        {
            CardNumber = "4222222222222222",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };

        // Act
        var result = await controller.CreatePaymentAsync(request);

        // Assert
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var payment = Assert.IsType<PostPaymentResponse>(created.Value);
        Assert.Equal(PaymentStatus.Declined, payment.Status);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("gbp")]
    public async Task AcceptsSupportedCurrencyCodes(string currency)
    {
        var controller = new PaymentsController(
            new PaymentService(new PaymentsRepository(), new StubBankClient()),
            NullLogger<PaymentsController>.Instance);
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = currency,
            Amount = 150,
            Cvv = "123"
        };

        var result = await controller.CreatePaymentAsync(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var payment = Assert.IsType<PostPaymentResponse>(created.Value);
        Assert.Equal(currency.ToUpperInvariant(), payment.Currency);
    }

    [Fact]
    public async Task ReturnsServiceUnavailableWithoutCreatingPaymentWhenBankIsUnavailable()
    {
        // Arrange
        var repository = new PaymentsRepository();
        var bankClient = new StubBankClient(
            new { },
            HttpStatusCode.ServiceUnavailable);
        var controller = new PaymentsController(new PaymentService(repository, bankClient), NullLogger<PaymentsController>.Instance);

        var request = new PostPaymentRequest
        {
            CardNumber = "4000000000000000",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };

        // Act
        var result = await controller.CreatePaymentAsync(request);

        // Assert
        var statusCodeResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, statusCodeResult.StatusCode);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public async Task ReturnsBadGatewayAndDoesNotSaveWhenBankResponseOmitsAuthorized()
    {
        var repository = new PaymentsRepository();
        var bankClient = new StubBankClient(new { }, HttpStatusCode.OK);
        var controller = new PaymentsController(
            new PaymentService(repository, bankClient),
            NullLogger<PaymentsController>.Instance);

        var result = await controller.CreatePaymentAsync(CreateValidRequest());

        var statusCode = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal((int)HttpStatusCode.BadGateway, statusCode.StatusCode);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public async Task ReturnsBadGatewayAndDoesNotSaveWhenBankResponseIsMalformedJson()
    {
        var repository = new PaymentsRepository();
        var bankClient = new StubBankClient(
            new StringContent("{ invalid", System.Text.Encoding.UTF8, "application/json"),
            HttpStatusCode.OK);
        var controller = new PaymentsController(
            new PaymentService(repository, bankClient),
            NullLogger<PaymentsController>.Instance);

        var result = await controller.CreatePaymentAsync(CreateValidRequest());

        var statusCode = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal((int)HttpStatusCode.BadGateway, statusCode.StatusCode);
        Assert.Empty(repository.GetAll());
    }

    [Fact]
    public async Task ReturnsExistingPaymentWhenIdempotencyKeyIsReused()
    {
        // Arrange
        var repository = new PaymentsRepository();
        var bankClient = new StubBankClient(new { authorized = true }, HttpStatusCode.OK);
        var service = new PaymentService(repository, bankClient);
        var controller = new PaymentsController(service, NullLogger<PaymentsController>.Instance);

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123",
            IdempotencyKey = "same-key"
        };

        // Act
        var firstResult = await controller.CreatePaymentAsync(request);
        var secondResult = await controller.CreatePaymentAsync(request);

        // Assert
        var firstPayment = Assert.IsType<PostPaymentResponse>(Assert.IsType<CreatedAtActionResult>(firstResult.Result).Value);
        var secondPayment = Assert.IsType<PostPaymentResponse>(Assert.IsType<OkObjectResult>(secondResult.Result).Value);
        Assert.Equal(firstPayment.Id, secondPayment.Id);
        Assert.Single(repository.GetAll());
    }

    [Fact]
    public async Task ReturnsConflictWhenIdempotencyKeyIsReusedWithDifferentPaymentDetails()
    {
        var repository = new PaymentsRepository();
        var bankClient = new StubBankClient(new { authorized = true }, HttpStatusCode.OK);
        var controller = new PaymentsController(
            new PaymentService(repository, bankClient),
            NullLogger<PaymentsController>.Instance);
        var firstRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123",
            IdempotencyKey = "same-key"
        };
        var differentRequest = new PostPaymentRequest
        {
            CardNumber = "4222222222222222",
            ExpiryMonth = firstRequest.ExpiryMonth,
            ExpiryYear = firstRequest.ExpiryYear,
            Currency = firstRequest.Currency,
            Amount = firstRequest.Amount,
            Cvv = firstRequest.Cvv,
            IdempotencyKey = firstRequest.IdempotencyKey
        };

        var firstResult = await controller.CreatePaymentAsync(firstRequest);
        var secondResult = await controller.CreatePaymentAsync(differentRequest);

        Assert.IsType<CreatedAtActionResult>(firstResult.Result);
        var conflict = Assert.IsType<ConflictObjectResult>(secondResult.Result);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(1, bankClient.CallCount);
        Assert.Single(repository.GetAll());
    }

    [Fact]
    public async Task ReturnsBadRequestWhenIdempotencyKeyExceedsMaximumLength()
    {
        var bankClient = new StubBankClient(new { authorized = true }, HttpStatusCode.OK);
        var controller = new PaymentsController(new PaymentService(new PaymentsRepository(), bankClient), NullLogger<PaymentsController>.Instance);

        var request = CreateValidRequest();
        request.IdempotencyKey = new string('a', 256);

        var result = await controller.CreatePaymentAsync(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal((int)HttpStatusCode.BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task ReturnsExistingPaymentWhenIdempotencyKeyIsReusedWithDifferentCvv()
    {
        var repository = new PaymentsRepository();
        var bankClient = new StubBankClient(new { authorized = true }, HttpStatusCode.OK);
        var service = new PaymentService(repository, bankClient);
        var controller = new PaymentsController(service, NullLogger<PaymentsController>.Instance);

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123",
            IdempotencyKey = "same-key"
        };

        var retryRequest = new PostPaymentRequest
        {
            CardNumber = request.CardNumber,
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency,
            Amount = request.Amount,
            Cvv = "999",
            IdempotencyKey = request.IdempotencyKey
        };

        var firstResult = await controller.CreatePaymentAsync(request);
        var secondResult = await controller.CreatePaymentAsync(retryRequest);

        var firstPayment = Assert.IsType<PostPaymentResponse>(Assert.IsType<CreatedAtActionResult>(firstResult.Result).Value);
        var secondPayment = Assert.IsType<PostPaymentResponse>(Assert.IsType<OkObjectResult>(secondResult.Result).Value);
        Assert.Equal(firstPayment.Id, secondPayment.Id);
        Assert.Single(repository.GetAll());
    }

    [Theory]
    [InlineData("4111", 12, 2099, "GBP", 150, "123")]
    [InlineData("4111111111111", 12, 2099, "GBP", 150, "123")]
    [InlineData("41111111111111111111", 12, 2099, "GBP", 150, "123")]
    [InlineData("4111 1111 1111 1111", 12, 2099, "GBP", 150, "123")]
    [InlineData("4111abcd11111111", 12, 2099, "GBP", 150, "123")]
    [InlineData("4111111111111111", 13, 2099, "GBP", 150, "123")]
    [InlineData("4111111111111111", 12, 1899, "GBP", 150, "123")]
    [InlineData("4111111111111111", 12, 2099, "GBP ", 150, "123")]
    [InlineData("4111111111111111", 12, 2099, "JPY", 150, "123")]
    [InlineData("4111111111111111", 12, 2099, "GBP", 0, "123")]
    [InlineData("4111111111111111", 12, 2099, "GBP", -10, "123")]
    [InlineData("4111111111111111", 12, 2099, "GBP", 150, "12")]
    [InlineData("4111111111111111", 12, 2099, "GBP", 150, "12345")]
    [InlineData("4111111111111111", 12, 2099, "GBP", 150, "1a3")]
    public async Task ReturnsBadRequestForInvalidPaymentPayloads(string cardNumber, int expiryMonth, int expiryYear, string currency, int amount, string cvv)
    {
        // Arrange
        var bankClient = new StubBankClient();
        var controller = new PaymentsController(new PaymentService(new PaymentsRepository(), bankClient), NullLogger<PaymentsController>.Instance);
        var request = new PostPaymentRequest
        {
            CardNumber = cardNumber,
            ExpiryMonth = expiryMonth,
            ExpiryYear = expiryYear,
            Currency = currency,
            Amount = amount,
            Cvv = cvv
        };

        // Act
        var result = await controller.CreatePaymentAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var rejection = Assert.IsType<PaymentRejectionResponse>(badRequestResult.Value);
        Assert.Equal(PaymentStatus.Rejected, rejection.Status);
        Assert.Equal(0, bankClient.CallCount);
    }

    [Theory]
    [InlineData(-1)]
    public async Task ReturnsBadRequestWhenExpiryMonthIsInThePast(int monthOffset)
    {
        var expiry = DateTime.UtcNow.AddMonths(monthOffset);
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };
        request.ExpiryMonth = expiry.Month;
        request.ExpiryYear = expiry.Year;

        var controller = new PaymentsController(
            new PaymentService(new PaymentsRepository(), new StubBankClient()),
            NullLogger<PaymentsController>.Instance);

        var result = await controller.CreatePaymentAsync(request);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AcceptsExpiryInTheCurrentMonth()
    {
        var currentMonth = DateTime.UtcNow;
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = currentMonth.Month,
            ExpiryYear = currentMonth.Year,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };
        var controller = new PaymentsController(
            new PaymentService(new PaymentsRepository(), new StubBankClient()),
            NullLogger<PaymentsController>.Instance);

        var result = await controller.CreatePaymentAsync(request);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task ReturnsBadRequestWhenPaymentRequestIsIncomplete()
    {
        // Arrange
        var controller = new PaymentsController(new PaymentService(new PaymentsRepository(), new StubBankClient()), NullLogger<PaymentsController>.Instance);
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };

        // Act
        var result = await controller.CreatePaymentAsync(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var rejection = Assert.IsType<PaymentRejectionResponse>(badRequestResult.Value);
        Assert.Equal(PaymentStatus.Rejected, rejection.Status);
    }

    private sealed class StubBankClient : IBankClient
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object _responseBody;
        private readonly HttpContent? _responseContent;

        public int CallCount { get; private set; }
        public object? LastPayload { get; private set; }

        public StubBankClient(object? responseBody = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody ?? new { authorized = true };
            _statusCode = statusCode;
        }

        public StubBankClient(HttpContent responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = new { };
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        public Task<HttpResponseMessage> SubmitPaymentAsync(object payload)
        {
            CallCount++;
            LastPayload = payload;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = _responseContent ?? JsonContent.Create(_responseBody)
            };

            return Task.FromResult(response);
        }
    }

    private static PostPaymentRequest CreateValidRequest()
    {
        return new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = DateTime.UtcNow.Year + 1,
            Currency = "GBP",
            Amount = 150,
            Cvv = "123"
        };
    }
}