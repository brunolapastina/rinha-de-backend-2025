using System;

namespace PaymentGateway;

public class PaymentProcessorHealthServer : IPaymentProcessorsHealth
{
   public bool DefaultFailing {get; set;} = false;

   public int DefaultMinRespTime {get; set;} = 0;

   public bool FallbackFailing {get; set;} = false;

   public int FallbackMinRespTime {get; set;} = 0;
}
