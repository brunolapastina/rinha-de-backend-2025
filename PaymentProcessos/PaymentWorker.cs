using System;

namespace PaymentProcessor;

public class PaymentWorker(PaymentsQueue _paymentQueue) : BackgroundService
{
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      while (await _paymentQueue.Reader.WaitToReadAsync(stoppingToken))
      {
         while (_paymentQueue.Reader.TryRead(out PaymentRequest request))
         {
            await ProcessPayment(request, stoppingToken);
         }
      }
   }

   private async Task ProcessPayment(PaymentRequest paymentReques, CancellationToken cancellationToken)
   {
      await Task.Delay(10, cancellationToken);
   }
}
