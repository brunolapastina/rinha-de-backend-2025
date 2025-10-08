using System.Net;

namespace PaymentGateway.Test;

public class PaymentProcessorHttpMessageHandlerMock : HttpMessageHandler
{
   public bool IsFailing { get; set; } = false;
   public int PaymentsCountSuccess { get; private set; } = 0;
   public int PaymentsCountError { get; private set; } = 0;
   public PaymentProcessorRequest LastPaymentProcessedSuccessfully { get; private set; }

   public void ResetCounters()
   {
      PaymentsCountSuccess = 0;
      PaymentsCountError = 0;
      LastPaymentProcessedSuccessfully = default;
   }

   protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
   {
      if (request.RequestUri!.AbsolutePath.Equals("/payments", StringComparison.OrdinalIgnoreCase))
      {
         if (IsFailing || request.Content is null)
         {
            PaymentsCountError++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
               Content = new StringContent("Error")
            };
         }
         else
         {
            LastPaymentProcessedSuccessfully = await request.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.PaymentProcessorRequest, cancellationToken);
            PaymentsCountSuccess++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
               Content = new StringContent("Payment Succedded")
            };
         }
      }

      return new HttpResponseMessage(HttpStatusCode.NotFound);
   }
}
