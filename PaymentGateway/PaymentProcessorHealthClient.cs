using System.Threading;

namespace PaymentGateway;

public sealed class PaymentProcessorHealthClient : PaymentProcessorsHealth, IAsyncDisposable, IDisposable
{
   private readonly ILogger<PaymentProcessorHealthClient> _logger;
   private readonly IStorageService _storageService;
   private readonly Timer _timer;

   public PaymentProcessorHealthClient(ILogger<PaymentProcessorHealthClient> logger, IStorageService storageService)
   {
      _logger = logger;
      _storageService = storageService;

      _timer = new Timer(UpdateHealthData, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
   }

   public void Dispose()
   {
      _timer.Dispose();
   }

   public async ValueTask DisposeAsync()
   {
      await _timer.DisposeAsync();
   }

   public override PaymentProcessor GetPreferedPaymentProcessor()
   {
      return GetPreferedPaymentProcessorCommonLogic();
   }

   private async void UpdateHealthData(object? state)
   {
      try
      {
         var health = await _storageService.GetHealthData();

         if(health is null)
         {
            _logger.LogWarning("No health data available to client");
            return;
         }

         HealthData.LastUpdate = health.LastUpdate;
         HealthData.DefaultFailing = health.DefaultFailing;
         HealthData.DefaultMinRespTime = health.DefaultMinRespTime;
         HealthData.FallbackFailing = health.FallbackFailing;
         HealthData.FallbackMinRespTime = health.FallbackMinRespTime;

         _logger.LogInformation("[Health] [Default -> Failing:{Failing} MinRespTime:{MinRespTime}] [Fallback -> Failing:{Failing} MinRespTime:{MinRespTime}] -> Prefered:{PreferedProcessor}", 
            HealthData.DefaultFailing, HealthData.DefaultMinRespTime, HealthData.FallbackFailing, HealthData.FallbackMinRespTime, 
            GetPreferedPaymentProcessorCommonLogic());
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Error getting payment processors health");
      }
   }
}