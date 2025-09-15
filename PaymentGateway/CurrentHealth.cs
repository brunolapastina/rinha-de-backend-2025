using System;

namespace PaymentGateway;

public class CurrentHealth
{
   public bool DefaultFailing { get; set; } = false;
   public int DefaultMinResponseTime { get; set; } = 0;

   public bool FallbackFailing { get; set; } = false;
   public int FallbackMinResponseTime { get; set; } = 0;

   public bool HasFunctioningProcessor => !DefaultFailing || !FallbackFailing;

   public PaymentProcessor GetBestPaymentProcessor()
   {
      if (!DefaultFailing)
      {
         return PaymentProcessor.Default;
      }
      else if (!FallbackFailing)
      {
         return PaymentProcessor.Fallback;
      }
      else
      {
         return PaymentProcessor.None;
      }
   }
}

public enum PaymentProcessor
{
   None,
   Default,
   Fallback
}
