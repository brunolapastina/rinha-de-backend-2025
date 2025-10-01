using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentGateway;

public class PaymentWorker : BackgroundService
{
   private readonly ILogger<PaymentWorker> _logger;
   private readonly bool _startFresh;
   private readonly int _workerLoopCount;
   private readonly PaymentsQueue _paymentQueue;
   private readonly IStorageService _storageService;
   private readonly PaymentProcessorService _defaultPaymentProcessor;
   private readonly PaymentProcessorService _fallbackPaymentProcessor;

   private readonly Counter<int> _defaultProcessedPaymentsCounter;
   private readonly Counter<int> _fallbackProcessedPaymentsCounter;
   private readonly Counter<int> _errorsCounter;
   private readonly Histogram<long> _defaultResponseTime;
   private readonly Histogram<long> _fallbackResponseTime;

   public PaymentWorker(
      ILogger<PaymentWorker> logger,
      IConfiguration configuration,
      IMeterFactory meterFactory,
      PaymentsQueue paymentQueue,
      IStorageService storageService,
      [FromKeyedServices("Default")] PaymentProcessorService defaultPaymentProcessor,
      [FromKeyedServices("Fallback")] PaymentProcessorService fallbackPaymentProcessor)
   {
      _logger = logger;
      _paymentQueue = paymentQueue;
      _storageService = storageService;
      _defaultPaymentProcessor = defaultPaymentProcessor;
      _fallbackPaymentProcessor = fallbackPaymentProcessor;

      _startFresh = configuration.GetValue("StartFresh", false);
      _workerLoopCount = configuration.GetValue("NumOfProcessingTaks", 8);

      var meter = meterFactory.Create("PaymentGateway");
      _defaultProcessedPaymentsCounter = meter.CreateCounter<int>(
         name: "default_processed_payments",
         unit: "payments",
         description: "Total number of payments that have been processed by de Default Processor");

      _fallbackProcessedPaymentsCounter = meter.CreateCounter<int>(
         name: "fallback_processed_payments",
         unit: "payments",
         description: "Total number of payments that have been processed by the Fallback Processor");

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
      if (_startFresh)
      {
         await _defaultPaymentProcessor.PurgePayments(stoppingToken);
         await _fallbackPaymentProcessor.PurgePayments(stoppingToken);
         await _storageService.ClearAllTransactions();
      }

      _logger.LogInformation("Starting {WorkerLoopCount} processing loops", _workerLoopCount);

      var workers = Enumerable
         .Range(0, _workerLoopCount)
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
            var res = await ProcessPayment(request, cancellationToken);
            if (!res)
            {  // Error processing payment, so enqueue it back
               await _paymentQueue.Enqueue(request);
            }
         }
      }

      _logger.LogDebug("Ended loop ID={Id}", id);
   }

   private readonly Stopwatch _sw = new();
   private async Task<bool> ProcessPayment(PaymentRequest paymentRequest, CancellationToken cancellationToken)
   {
      var ppReq = new PaymentProcessorRequest(paymentRequest.CorrelationId, paymentRequest.Amount, DateTimeOffset.UtcNow);

      _sw.Restart();
      var res = await _defaultPaymentProcessor.SendPayment(ppReq, cancellationToken);
      _sw.Stop();
      if (res)
      {
         _defaultProcessedPaymentsCounter.Add(1);
         _defaultResponseTime.Record(_sw.ElapsedMilliseconds);
         await _storageService.AddTransaction(ppReq.RequestedAt, ppReq.CorrelationId, ppReq.Ammount, PaymentProcessor.Default);
      }
      else
      {
         ppReq = new PaymentProcessorRequest(paymentRequest.CorrelationId, paymentRequest.Amount, DateTimeOffset.UtcNow);
         _sw.Restart();
         res = await _fallbackPaymentProcessor.SendPayment(ppReq, cancellationToken);
         _sw.Stop();
         if (res)
         {
            _fallbackProcessedPaymentsCounter.Add(1);
            _fallbackResponseTime.Record(_sw.ElapsedMilliseconds);
            await _storageService.AddTransaction(ppReq.RequestedAt, ppReq.CorrelationId, ppReq.Ammount, PaymentProcessor.Fallback);
         }
         else
         {
            _errorsCounter.Add(1);
         }
      }      
      
      return res;
   }
}
