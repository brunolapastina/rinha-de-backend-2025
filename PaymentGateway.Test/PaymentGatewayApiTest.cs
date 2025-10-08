using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace PaymentGateway.Test;

public class PaymentGatewayApiTest : IClassFixture<WebApplicationFactory<Program>>
{
   private readonly Mock<IStorageService> _storageMock = new();
   private readonly PaymentProcessorHttpMessageHandlerMock _defaultPaymentProcessor = new();
   private readonly PaymentProcessorHttpMessageHandlerMock _fallbackPaymentProcessor = new();
   private readonly WebApplicationFactory<Program> _factory;

   public PaymentGatewayApiTest(WebApplicationFactory<Program> factory)
   {
      _storageMock.Setup(x => x.AddTransaction(It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<PaymentProcessor>()))
          .Returns(Task.CompletedTask);

      _factory = factory.WithWebHostBuilder(builder =>
         builder.ConfigureServices(services =>
         {
            // Remove the real dependency
            services.RemoveAll<IStorageService>();

            // Replace with mocked handler
            services.AddSingleton(_storageMock.Object);

            ConfigureMokedPaymentProcessor(services, _defaultPaymentProcessor, "Default");
            ConfigureMokedPaymentProcessor(services, _fallbackPaymentProcessor, "Fallback");
         }));
   }

   private static void ConfigureMokedPaymentProcessor(IServiceCollection services, PaymentProcessorHttpMessageHandlerMock mock, string instance)
   {
      services.AddHttpClient($"PaymentProcessor:{instance}")
         .ConfigurePrimaryHttpMessageHandler(() => mock)
         .ConfigureHttpClient(client =>
         {
            client.BaseAddress = new Uri($"http://dummy.fakeapi.com/");
         });
   }

   private static async Task WaitForEmptyQueue(PaymentsQueue queue)
   {
      for (int i = 0; i < 1000; i++)
      {
         await Task.Delay(10);

         if (queue.IsQueueEmpty())
         {
            break;
         }
      }
   }

   [Fact]
   public async Task PostPayment_Should_ReturnOkAndProcessOnDefault()
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

      await WaitForEmptyQueue(queue);

      Assert.Equal(1, _defaultPaymentProcessor.PaymentsCountSuccess);
      Assert.Equal(0, _defaultPaymentProcessor.PaymentsCountError);
      Assert.Equal(payload.CorrelationId, _defaultPaymentProcessor.LastPaymentProcessedSuccessfully.CorrelationId);
      Assert.Equal(payload.Amount, _defaultPaymentProcessor.LastPaymentProcessedSuccessfully.Ammount);

      _storageMock.Verify(
          it => it.AddTransaction(_defaultPaymentProcessor.LastPaymentProcessedSuccessfully.RequestedAt, payload.CorrelationId, payload.Amount, PaymentProcessor.Default),
          Times.Once,
          "Transaction incorrectly added to the storage");
   }

   [Fact]
   public async Task PostPaymentWithDefaultFailing_Should_ReturnOkAndProcessOnFallback()
   {
      using var client = _factory.CreateClient();
      var queue = _factory.Services.GetService<PaymentsQueue>();
      Assert.NotNull(queue);

      _defaultPaymentProcessor.IsFailing = true;

      var payload = new PaymentRequest(Guid.NewGuid().ToString(), 19.9m);
      var response = await client.PostAsJsonAsync("/payments", payload);

      // The call should never fail
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);

      // For maximum performance, we want the body to be empty
      Assert.Empty(await response.Content.ReadAsStringAsync());

      await WaitForEmptyQueue(queue);

      _storageMock.Verify(
          it => it.AddTransaction(It.IsAny<DateTimeOffset>(), payload.CorrelationId, payload.Amount, It.IsAny<PaymentProcessor>()),
          Times.Once,
          "Transaction incorrectly added to the storage");


   }
}
