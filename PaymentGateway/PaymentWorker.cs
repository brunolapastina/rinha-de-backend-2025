using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentGateway;

public class PaymentWorker : BackgroundService
{
   private readonly ILogger<PaymentWorker> _logger;
   private readonly bool _startFresh;
   private readonly int _processingBatchSize;
   private readonly PaymentsQueue _paymentQueue;
   private readonly IStorageService _storageService;
   private readonly PaymentProcessorService _defaultPaymentProcessor;
   private readonly PaymentProcessorService _fallbackPaymentProcessor;

   private readonly Counter<int> _defaultProcessedPaymentsCounter;
   private readonly Counter<int> _fallbackProcessedPaymentsCounter;
   private readonly Counter<int> _errorsCounter;
   private readonly Histogram<long> _defaultResponseTime;
   private readonly Histogram<long> _fallbackResponseTime;

   private int _lastBatchSize = 0;

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
      _processingBatchSize = configuration.GetValue("ProcessingBatchSize", 100);

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

      meter.CreateObservableCounter(
         name: "last_batch_size",
         observeValue: () => new Measurement<int>(_lastBatchSize),
         unit: "requests",
         description: "Size of the last batch processed"
      );

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
         _logger.LogInformation("Starting fresh");
         await _defaultPaymentProcessor.PurgePayments(stoppingToken);
         await _fallbackPaymentProcessor.PurgePayments(stoppingToken);
         await _storageService.ClearAllTransactions();
      }

      _logger.LogInformation("Starting processing loops");

      var requests = new List<PaymentRequest>(_processingBatchSize);

      while (await _paymentQueue.Reader.WaitToReadAsync(stoppingToken))
      {
         while ((requests.Count < _processingBatchSize) && 
                _paymentQueue.Reader.TryRead(out PaymentRequest? request))
         {
            requests.Add(request);
         }

         _lastBatchSize = requests.Count;

         var beingProcessedTaks = requests.Select(async req =>
         {
            var res = await ProcessPayment(req, stoppingToken);
            if (!res)
            {  // Error processing payment, so enqueue it back
               await _paymentQueue.Enqueue(req);
            }
         });

         await Task.WhenAll(beingProcessedTaks);

         requests.Clear();
      }

      _logger.LogInformation("All processing loops ended");
   }

   private async Task<bool> ProcessPayment(PaymentRequest paymentRequest, CancellationToken cancellationToken)
   {
      var ppReq = new PaymentProcessorRequest(paymentRequest.CorrelationId, paymentRequest.Amount, DateTimeOffset.UtcNow);

      var sw = Stopwatch.StartNew();
      var res = await _defaultPaymentProcessor.SendPayment(ppReq, cancellationToken);
      sw.Stop();
      if (res)
      {
         _defaultProcessedPaymentsCounter.Add(1);
         _defaultResponseTime.Record(sw.ElapsedMilliseconds);
         await _storageService.AddTransaction(ppReq.RequestedAt, ppReq.CorrelationId, ppReq.Ammount, PaymentProcessor.Default);
      }
      else
      {
         // Recreate request to update the RequestedAt field
         ppReq = new PaymentProcessorRequest(paymentRequest.CorrelationId, paymentRequest.Amount, DateTimeOffset.UtcNow);
         sw.Restart();
         res = await _fallbackPaymentProcessor.SendPayment(ppReq, cancellationToken);
         sw.Stop();
         if (res)
         {
            _fallbackProcessedPaymentsCounter.Add(1);
            _fallbackResponseTime.Record(sw.ElapsedMilliseconds);
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
