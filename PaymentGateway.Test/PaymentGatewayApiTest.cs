using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace PaymentGateway.Test;

public class MockHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.Equals("/payments", StringComparison.OrdinalIgnoreCase))
        {
            
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Payment Succedded")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}


public class PaymentGatewayApiTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly Mock<IStorageService> _mock = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PaymentGatewayApiTest(WebApplicationFactory<Program> factory)
    {
        _mock.Setup(x => x.AddTransaction(It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<PaymentProcessor>()))
            .Returns(Task.CompletedTask);

        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // Remove the real dependency
                services.RemoveAll<IStorageService>();
                services.RemoveAll<IHttpClientFactory>();

                // Replace with mocked handler
                services.AddSingleton<IStorageService>(_mock.Object);
                services.AddHttpClient("PaymentProcessor")
                    .ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler());
            }));

    }

    [Fact]
    public async Task PostPaymentShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        var queue = _factory.Services.GetService<PaymentsQueue>();
        Assert.NotNull(queue);

        var payload = new PaymentRequest(Guid.NewGuid().ToString(), 19.9m);
        var response = await client.PostAsJsonAsync("/payments", payload);

        // The call should never fail
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // For maximum performance, we want the body to be empty
        Assert.Empty(await response.Content.ReadAsStringAsync());

        for (int i = 0; i < 1000; i++)
        {
            await Task.Delay(10);

            if (queue.IsQueueEmpty())
            {
                break;
            }
        }

        _mock.Verify(
            it => it.AddTransaction(It.IsAny<DateTimeOffset>(), payload.CorrelationId, payload.Amount, It.IsAny<PaymentProcessor>()),
            Times.Once,
            "Transaction incorrectly added to the storage");
    }
}
