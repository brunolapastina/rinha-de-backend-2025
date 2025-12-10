namespace PaymentGateway;

public sealed class PaymentProcessorsHealth : IAsyncDisposable, IDisposable
{
   public enum HealthCheckMode { Server, Client }

   private readonly ILogger<PaymentProcessorsHealth> _logger;
   private readonly IStorageService _storageService;
   private readonly PaymentProcessorService _defaultPaymentProcessor;
   private readonly PaymentProcessorService _fallbackPaymentProcessor;
   private readonly Timer _timer;

   public HealthData HealthData { get; set; } = new();

   public PaymentProcessorsHealth(
      ILogger<PaymentProcessorsHealth> logger,
      IConfiguration configuration,
      IStorageService storageService,
      [FromKeyedServices("Default")] PaymentProcessorService defaultPaymentProcessor,
      [FromKeyedServices("Fallback")] PaymentProcessorService fallbackPaymentProcessor)
   {
      _logger = logger;
      _storageService = storageService;
      _defaultPaymentProcessor = defaultPaymentProcessor;
      _fallbackPaymentProcessor = fallbackPaymentProcessor;

      var healthCheckMode = configuration.GetValue<HealthCheckMode?>("HealthCheckMode", null) ??
         throw new InvalidConfigurationException("Health check mode has to be set to either Server or Client");

      if (healthCheckMode == HealthCheckMode.Server)
      {
         _timer = new Timer(UpdateHealthDataServer, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
      }
      else
      {
         _timer = new Timer(UpdateHealthDataClient, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
      }
   }

   public void Dispose()
   {
      _timer.Dispose();
   }

   public async ValueTask DisposeAsync()
   {
      await _timer.DisposeAsync();
   }

   public PaymentProcessor GetPreferedPaymentProcessor()
   {
      if (HealthData.DefaultFailing)
      {
         return PaymentProcessor.Fallback;
      }
      else if (HealthData.DefaultMinRespTime < 502)
      {
         return PaymentProcessor.Default;
      }
      else if (!HealthData.FallbackFailing && (HealthData.FallbackMinRespTime < HealthData.DefaultMinRespTime * 3))
      {
         return PaymentProcessor.Fallback;
      }
      else
      {
         return PaymentProcessor.Default;
      }
   }

   private async void UpdateHealthDataServer(object? state)
   {
      try
      {
         var defaultHealth = await _defaultPaymentProcessor.GetServiceHealth(default);
         var fallbackHealth = await _fallbackPaymentProcessor.GetServiceHealth(default);

         if (defaultHealth.HasValue || fallbackHealth.HasValue)
         {
            HealthData.LastUpdate = DateTime.Now;
         }

         if (defaultHealth.HasValue)
         {
            HealthData.DefaultFailing = defaultHealth.Value.Failing;
            HealthData.DefaultMinRespTime = defaultHealth.Value.MinResponseTime;
         }

         if (fallbackHealth.HasValue)
         {
            HealthData.FallbackFailing = fallbackHealth.Value.Failing;
            HealthData.FallbackMinRespTime = fallbackHealth.Value.MinResponseTime;
         }

         _logger.LogInformation("[Health Server] [Default -> Failing:{Failing} MinRespTime:{MinRespTime}] [Fallback -> Failing:{Failing} MinRespTime:{MinRespTime}] -> Prefered:{PreferedProcessor}",
            HealthData.DefaultFailing, HealthData.DefaultMinRespTime, HealthData.FallbackFailing, HealthData.FallbackMinRespTime,
            GetPreferedPaymentProcessor());

         await _storageService.SetHealthData(HealthData);
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Error checking payment processors health");
      }
   }

   private async void UpdateHealthDataClient(object? state)
   {
      try
      {
         var health = await _storageService.GetHealthData();

         if (health is null)
         {
            _logger.LogWarning("No health data available to client");
            return;
         }

         HealthData.LastUpdate = health.LastUpdate;
         HealthData.DefaultFailing = health.DefaultFailing;
         HealthData.DefaultMinRespTime = health.DefaultMinRespTime;
         HealthData.FallbackFailing = health.FallbackFailing;
         HealthData.FallbackMinRespTime = health.FallbackMinRespTime;

         _logger.LogInformation("[Health Client] [Default -> Failing:{Failing} MinRespTime:{MinRespTime}] [Fallback -> Failing:{Failing} MinRespTime:{MinRespTime}] -> Prefered:{PreferedProcessor}",
            HealthData.DefaultFailing, HealthData.DefaultMinRespTime, HealthData.FallbackFailing, HealthData.FallbackMinRespTime,
            GetPreferedPaymentProcessor());
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Error getting payment processors health");
      }
   }
}
