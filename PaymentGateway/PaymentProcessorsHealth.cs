namespace PaymentGateway;

public abstract class PaymentProcessorsHealth
{
   public HealthData HealthData { get; set; } = new HealthData();

   public abstract PaymentProcessor GetPreferedPaymentProcessor();

   protected PaymentProcessor GetPreferedPaymentProcessorCommonLogic()
   {
      if (HealthData.DefaultFailing)
      {
         return PaymentProcessor.Fallback;
      }
      else if (HealthData.DefaultMinRespTime > 1000 && (3 * HealthData.FallbackMinRespTime < (2 * HealthData.DefaultMinRespTime))) //Fallback < (2/3) * Default
      {
         return PaymentProcessor.Fallback;
      }

      return PaymentProcessor.Default;
   }
}
