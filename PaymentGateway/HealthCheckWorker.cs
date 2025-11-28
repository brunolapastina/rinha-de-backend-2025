using System;

namespace PaymentGateway;

public class HealthCheckWorker : BackgroundService
{
   private readonly ILogger<HealthCheckWorker> _logger;
   private readonly PaymentProcessorHealthServer _ppHealth;
   private readonly PaymentProcessorService _defaultPaymentProcessor;
   private readonly PaymentProcessorService _fallbackPaymentProcessor;

   public HealthCheckWorker(
      ILogger<HealthCheckWorker> logger,
      PaymentProcessorHealthServer ppHealth,
      [FromKeyedServices("Default")] PaymentProcessorService defaultPaymentProcessor,
      [FromKeyedServices("Fallback")] PaymentProcessorService fallbackPaymentProcessor)
   {
      _logger = logger;
      _ppHealth = ppHealth;
      _defaultPaymentProcessor = defaultPaymentProcessor;
      _fallbackPaymentProcessor = fallbackPaymentProcessor;
   }

   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      while (!stoppingToken.IsCancellationRequested)
      {
         try
         {
            var defaultHealth = await _defaultPaymentProcessor.GetServiceHealth(stoppingToken);
            var fallbackHealth = await _fallbackPaymentProcessor.GetServiceHealth(stoppingToken);

            if(defaultHealth.HasValue)
            {
               _ppHealth.DefaultFailing = defaultHealth.Value.Failing;
               _ppHealth.DefaultMinRespTime = defaultHealth.Value.MinResponseTime;
            }

            if(fallbackHealth.HasValue)
            {
               _ppHealth.FallbackFailing = fallbackHealth.Value.Failing;
               _ppHealth.FallbackMinRespTime = fallbackHealth.Value.MinResponseTime;
            }

            _logger.LogInformation("[Health] [Default -> Failing:{Failing} MinRespTime:{MinRespTime}] [Fallback -> Failing:{Failing} MinRespTime:{MinRespTime}] -> Prefered:{PreferedProcessor}", 
               _ppHealth.DefaultFailing, _ppHealth.DefaultMinRespTime, _ppHealth.FallbackFailing, _ppHealth.FallbackMinRespTime, 
               (_ppHealth as IPaymentProcessorsHealth).GetPreferedPaymentProcessor());

            await Task.Delay(5000, stoppingToken);
         }
         catch (Exception ex)
         {
            if(!stoppingToken.IsCancellationRequested)
            {
               _logger.LogWarning(ex, "Error checking payment processors health");
            }
         }
      }
   }
}
