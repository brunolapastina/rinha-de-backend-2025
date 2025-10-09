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
      _storageMock.Setup(x => x.AddTransaction(It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<PaymentProcessor>()))
          .Returns(Task.CompletedTask);

      using var client = _factory.CreateClient();
      var queue = _factory.Services.GetService<PaymentsQueue>();
      Assert.NotNull(queue);

      _defaultPaymentProcessor.IsFailing = false;
      _fallbackPaymentProcessor.IsFailing = false;

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
      _storageMock.Setup(x => x.AddTransaction(It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<PaymentProcessor>()))
          .Returns(Task.CompletedTask);

      using var client = _factory.CreateClient();
      var queue = _factory.Services.GetService<PaymentsQueue>();
      Assert.NotNull(queue);

      _defaultPaymentProcessor.IsFailing = true;
      _fallbackPaymentProcessor.IsFailing = false;

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

   [Fact]
   public async Task PostPaymentWithBothFailing_Should_ReturnOkAndReenqueRequest()
   {
      _storageMock.Setup(x => x.AddTransaction(It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<PaymentProcessor>()))
          .Returns(Task.CompletedTask);

      using var client = _factory.CreateClient();
      var queue = _factory.Services.GetService<PaymentsQueue>();
      Assert.NotNull(queue);

      _defaultPaymentProcessor.IsFailing = true;
      _fallbackPaymentProcessor.IsFailing = true;

      var payload = new PaymentRequest(Guid.NewGuid().ToString(), 19.9m);
      var response = await client.PostAsJsonAsync("/payments", payload);

      // The call should never fail
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);

      // For maximum performance, we want the body to be empty
      Assert.Empty(await response.Content.ReadAsStringAsync());

      _storageMock.Verify(
          it => it.AddTransaction(It.IsAny<DateTimeOffset>(), payload.CorrelationId, payload.Amount, It.IsAny<PaymentProcessor>()),
          Times.Never,
          "Transaction incorrectly added to the storage");

      Assert.Equal(0, _defaultPaymentProcessor.PaymentsCountSuccess);
      Assert.Equal(0, _fallbackPaymentProcessor.PaymentsCountSuccess);

      Assert.True(_defaultPaymentProcessor.PaymentsCountError > 0, "Default payment processor not called");
      Assert.True(_fallbackPaymentProcessor.PaymentsCountError > 0, "Fallback payment processor not called");
   }

   [Fact]
   public async Task GetPaymentSummary_With_NoPaymentAndNoParametes_Should_ReturnOkAndZeros()
   {
      using var client = _factory.CreateClient();

      var response = await client.GetAsync("/payments-summary");

      // The call should never fail
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);

      var summary = await response.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.PaymentSummaryResponse);

      Assert.Equal(0, summary.Default.TotalAmount);
      Assert.Equal(0, summary.Default.TotalRequests);

      Assert.Equal(0, summary.Fallback.TotalAmount);
      Assert.Equal(0, summary.Fallback.TotalRequests);
   }

   [Fact]
   public async Task GetPaymentSummary_With_PaymentAndNoParametes_Should_ReturnOkAndData()
   {
      PaymentStorage[] payments = [
            new PaymentStorage(Guid.NewGuid().ToString(), 19.9m, PaymentProcessor.Default),
            new PaymentStorage(Guid.NewGuid().ToString(), 4.7m, PaymentProcessor.Default),
            new PaymentStorage(Guid.NewGuid().ToString(), 18.8m, PaymentProcessor.Fallback)
          ];
      _storageMock.Setup(x => x.GetSummary(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
          .ReturnsAsync(payments);

      using var client = _factory.CreateClient();

      var response = await client.GetAsync("/payments-summary");

      // The call should never fail
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);

      _storageMock.Verify(
         x => x.GetSummary(It.IsAny<DateTime?>(), It.IsAny<DateTime?>()),
         Times.Once(),
         "Summary not requested"
      );

      var summary = await response.Content.ReadFromJsonAsync<PaymentSummaryResponse>();

      var grp = payments.GroupBy(x => x.PaymentProcessor);
      Assert.Equal(grp.FirstOrDefault(x=>x.Key == PaymentProcessor.Default)?.Sum(x=>x.Ammount) ?? 0, summary.Default.TotalAmount);
      Assert.Equal(grp.FirstOrDefault(x=>x.Key == PaymentProcessor.Default)?.Count() ?? 0, summary.Default.TotalRequests);

      Assert.Equal(grp.FirstOrDefault(x=>x.Key == PaymentProcessor.Fallback)?.Sum(x=>x.Ammount) ?? 0, summary.Fallback.TotalAmount);
      Assert.Equal(grp.FirstOrDefault(x=>x.Key == PaymentProcessor.Fallback)?.Count() ?? 0, summary.Fallback.TotalRequests);
   }
}
