using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentGateway;

public class PaymentWorker : BackgroundService
{
   const int WorkerLoopCount = 16;

   private readonly ILogger<PaymentWorker> _logger;
   private readonly PaymentsQueue _paymentQueue;
   private readonly PaymentProcessorService _defaultPaymentProcessor;
   private readonly PaymentProcessorService _fallbackPaymentProcessor;
   private Counter<int> _processedPaymentsCounter;
   private Counter<int> _errorsCounter;
   private Histogram<long> _defaultResponseTime;
   private Histogram<long> _fallbackResponseTime;

   public PaymentWorker(
      ILogger<PaymentWorker> logger,
      IMeterFactory meterFactory,
      PaymentsQueue paymentQueue,
      [FromKeyedServices("Default")] PaymentProcessorService defaultPaymentProcessor,
      [FromKeyedServices("Fallback")] PaymentProcessorService fallbackPaymentProcessor)
   {
      _logger = logger;
      _paymentQueue = paymentQueue;
      _defaultPaymentProcessor = defaultPaymentProcessor;
      _fallbackPaymentProcessor = fallbackPaymentProcessor;

      var meter = meterFactory.Create("PaymentGateway");
      _processedPaymentsCounter = meter.CreateCounter<int>(
         name: "processed_payments",
         unit: "payments",
         description: "Total number of payments that have been processed");

      _errorsCounter = meter.CreateCounter<int>(
         name: "processing_errors",
         unit: "payments",
         description: "Total number of errors that happened during processing of payments");

      _defaultResponseTime = meter.CreateHistogram<long>(
         name: "default_pp_response_time",
         unit: "ms",
         description: "Response time for the default payment processor"
      );

      _fallbackResponseTime = meter.CreateHistogram<long>(
         name: "fallback_pp_response_time",
         unit: "ms",
         description: "Response time for the fallback payment processor"
      );
   }

   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      _logger.LogInformation("Starting {WorkerLoopCount} processing loops", WorkerLoopCount);

      var workers = Enumerable
         .Range(0, WorkerLoopCount)
         .Select(async id => await ProcessingLoop(id, stoppingToken)) // generator
         .ToArray();

      await Task.WhenAll(workers);

      _logger.LogInformation("All processing loops ended");
   }

   private async Task ProcessingLoop(int id, CancellationToken cancellationToken)
   {
      _logger.LogDebug("Started loop ID={Id}", id);

      while (await _paymentQueue.Reader.WaitToReadAsync(cancellationToken))
      {
         while (_paymentQueue.Reader.TryRead(out PaymentRequest request))
         {
            await ProcessPayment(request, cancellationToken);
         }
      }

      _logger.LogDebug("Ended loop ID={Id}", id);
   }

   private readonly Stopwatch _sw = new();
   private async Task ProcessPayment(PaymentRequest paymentReques, CancellationToken cancellationToken)
   {
      _sw.Restart();
      var res = await _defaultPaymentProcessor.SendPayment(paymentReques, cancellationToken);
      _sw.Stop();

      if (res)
      {
         _processedPaymentsCounter.Add(1);
         _defaultResponseTime.Record(_sw.ElapsedMilliseconds);
      }
      else
      {
         _errorsCounter.Add(1);
      }
   }
}
