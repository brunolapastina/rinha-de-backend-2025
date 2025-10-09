using Microsoft.VisualBasic;

namespace PaymentGateway.Test;

public class StorageServiceTest
{
   public StorageServiceTest()
   {
   }

   [Fact]
   public void ConstructorWithInvalidConfig_Should_Throw()
   {
      var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
      builder.Services.AddSingleton<IStorageService, StorageService>();
      var app = builder.Build();

      Assert.Throws<InvalidConfigurationException>(() => app.Services.GetService<IStorageService>());
   }

   private static IStorageService? GetStorageService()
   {
      var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
      builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
      {
         { "RedisConfig", "localhost:6379" }
      })
      .AddEnvironmentVariables();
      builder.Services.AddSingleton<IStorageService, StorageService>();
      var app = builder.Build();

      return app.Services.GetService<IStorageService>();   
   }

   [Fact]
   public void ConstructorWithValidConfig_Should_NotThrow()
   {
      var storage = GetStorageService();
      Assert.NotNull(storage);
   }

   [Fact]
   public async Task AddTransaction_Should_CorrectlyAddTheTransaction()
   {
      var storage = GetStorageService();
      Assert.NotNull(storage);

      await storage.ClearAllTransactions();

      var requestAt = DateTimeOffset.Now;
      var correlationId = Guid.NewGuid().ToString();
      var ammount = 19.90m;
      var processor = PaymentProcessor.Default;

      await storage.AddTransaction(requestAt, correlationId, ammount, processor);

      var summary = await storage.GetSummary(null, null);
      Assert.Single(summary);

      Assert.Equal(correlationId, summary.Single().CorrelationId);
      Assert.Equal(ammount, summary.Single().Ammount);
      Assert.Equal(processor, summary.Single().PaymentProcessor);
   }

   [Fact]
   public async Task GetSummaryWithRange_Should_CorrectlyReturnData()
   {
      var storage = GetStorageService();
      Assert.NotNull(storage);

      await storage.ClearAllTransactions();

      var refDate = DateTimeOffset.Now;

      var data = new[]
      {
         new{ RequestAt = refDate.AddMinutes(-1), CorrelationId = Guid.NewGuid().ToString(), Ammount = 10.9m, Processor = PaymentProcessor.Default},
         new{ RequestAt = refDate.AddMinutes(0), CorrelationId = Guid.NewGuid().ToString(), Ammount = 11.9m, Processor = PaymentProcessor.Fallback},
         new{ RequestAt = refDate.AddMinutes(1), CorrelationId = Guid.NewGuid().ToString(), Ammount = 12.9m, Processor = PaymentProcessor.Default},
      };

      await storage.AddTransaction(data[0].RequestAt, data[0].CorrelationId, data[0].Ammount, data[0].Processor);
      await storage.AddTransaction(data[1].RequestAt, data[1].CorrelationId, data[1].Ammount, data[1].Processor);
      await storage.AddTransaction(data[2].RequestAt, data[2].CorrelationId, data[2].Ammount, data[2].Processor);
      
      var all = await storage.GetSummary(null, null);
      Assert.Equal(3, all.Length);

      var single = await storage.GetSummary(data[1].RequestAt.AddSeconds(-1).DateTime, data[1].RequestAt.AddSeconds(1).DateTime);
      Assert.Single(single);

      Assert.Equal(data[1].CorrelationId, single.Single().CorrelationId);
      Assert.Equal(data[1].Ammount, single.Single().Ammount);
      Assert.Equal(data[1].Processor, single.Single().PaymentProcessor);
   }

   [Fact]
   public async Task ClearAllTransactions_Should_ClearEverything()
   {
      var storage = GetStorageService();
      Assert.NotNull(storage);

      await storage.AddTransaction(DateTimeOffset.Now, Guid.NewGuid().ToString(), 19.90m, PaymentProcessor.Default);

      await storage.ClearAllTransactions();

      var summary = await storage.GetSummary(null, null);
      Assert.Empty(summary);
   }
}
