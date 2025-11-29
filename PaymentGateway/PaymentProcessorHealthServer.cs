using System;
using System.Threading;

namespace PaymentGateway;

public sealed class PaymentProcessorHealthServer : PaymentProcessorsHealth, IAsyncDisposable, IDisposable
{
   private readonly ILogger<PaymentProcessorHealthServer> _logger;
   private readonly IStorageService _storageService;
   private readonly PaymentProcessorService _defaultPaymentProcessor;
   private readonly PaymentProcessorService _fallbackPaymentProcessor;
   private readonly Timer _timer;

   public PaymentProcessorHealthServer(
      ILogger<PaymentProcessorHealthServer> logger, 
      IStorageService storageService,
      [FromKeyedServices("Default")] PaymentProcessorService defaultPaymentProcessor,
      [FromKeyedServices("Fallback")] PaymentProcessorService fallbackPaymentProcessor)
   {
      _logger = logger;
      _storageService = storageService;
      _defaultPaymentProcessor = defaultPaymentProcessor;
      _fallbackPaymentProcessor = fallbackPaymentProcessor;

      _timer = new Timer(UpdateHealthData, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
   }

   public void Dispose()
   {
      _timer.Dispose();
   }

   public async ValueTask DisposeAsync()
   {
      await _timer.DisposeAsync();
   }

   public override PaymentProcessor GetPreferedPaymentProcessor() => 
      GetPreferedPaymentProcessorCommonLogic();

   private async void UpdateHealthData(object? state)
   {
      try
      {
         var defaultHealth = await _defaultPaymentProcessor.GetServiceHealth(default);
         var fallbackHealth = await _fallbackPaymentProcessor.GetServiceHealth(default);

         if(defaultHealth.HasValue || fallbackHealth.HasValue)
         {
            HealthData.LastUpdate = DateTime.Now;
         }

         if(defaultHealth.HasValue)
         {
            HealthData.DefaultFailing = defaultHealth.Value.Failing;
            HealthData.DefaultMinRespTime = defaultHealth.Value.MinResponseTime;
         }

         if(fallbackHealth.HasValue)
         {
            HealthData.FallbackFailing = fallbackHealth.Value.Failing;
            HealthData.FallbackMinRespTime = fallbackHealth.Value.MinResponseTime;
         }

         _logger.LogInformation("[Health] [Default -> Failing:{Failing} MinRespTime:{MinRespTime}] [Fallback -> Failing:{Failing} MinRespTime:{MinRespTime}] -> Prefered:{PreferedProcessor}", 
            HealthData.DefaultFailing, HealthData.DefaultMinRespTime, HealthData.FallbackFailing, HealthData.FallbackMinRespTime, 
            GetPreferedPaymentProcessorCommonLogic());

         await _storageService.SetHealthData(HealthData);
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Error checking payment processors health");
      }
   }
}