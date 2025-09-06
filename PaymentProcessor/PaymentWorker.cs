namespace PaymentProcessor;

public class PaymentWorker(ILogger<PaymentWorker> _logger, PaymentsQueue _paymentQueue) : BackgroundService
{
   const int WorkerLoopCount = 6;
   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      _logger.LogInformation("Starting {WorkerLoopCount} processing loops", WorkerLoopCount);

      var workers = Enumerable.Range(0, WorkerLoopCount)
                       .Select(async i => await ProcessingLoop(i, stoppingToken)) // generator
                       .ToArray();

      await Task.WhenAll(workers);

      _logger.LogInformation("All processing loops ended");
   }

   private async Task ProcessingLoop(int id, CancellationToken cancellationToken)
   {
      _logger.LogInformation("Started loop ID={Id}", id);

      while (await _paymentQueue.Reader.WaitToReadAsync(cancellationToken))
      {
         while (_paymentQueue.Reader.TryRead(out PaymentRequest request))
         {
            await ProcessPayment(request, cancellationToken);
         }
      }
   }

   private async Task ProcessPayment(PaymentRequest paymentReques, CancellationToken cancellationToken)
   {
      await Task.Delay(10, cancellationToken);
   }
}
