using System;

namespace PaymentGateway;

public interface IPaymentProcessorsHealth
{
   public bool DefaultFailing {get;}
   public int DefaultMinRespTime {get;}
   public bool FallbackFailing {get;}
   public int FallbackMinRespTime {get;}

   public PaymentProcessor GetPreferedPaymentProcessor()
   {
      if(DefaultFailing)
      {
         return PaymentProcessor.Fallback;
      }
      else if( DefaultMinRespTime > 1000 && (3 * FallbackMinRespTime < (2 * DefaultMinRespTime))) //Fallback < (2/3) * Default
      {
         return PaymentProcessor.Fallback;
      }

      return PaymentProcessor.Default;
   }
}
