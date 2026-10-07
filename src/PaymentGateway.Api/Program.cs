using System.Text.Json.Serialization;

using PaymentGateway.Api.Interfaces;
using PaymentGateway.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var bankBaseAddressValue = builder.Configuration["Bank:BaseAddress"];
if (!Uri.TryCreate(bankBaseAddressValue, UriKind.Absolute, out var bankBaseAddress))
{
    throw new InvalidOperationException("Configuration 'Bank:BaseAddress' must be an absolute URI.");
}

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<IBankClient, BankClient>(client =>
{
    client.BaseAddress = bankBaseAddress;
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
