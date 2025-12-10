using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentGateway;

public class PaymentWorker : BackgroundService
{
   private enum TaskMode
   {
      DefaultOnly,
      MostHealthy
   }

   private readonly ILogger<PaymentWorker> _logger;
   private readonly bool _startFresh;
   private readonly int _workerLoopCount;
   private readonly int _percentageOfMostHealthy;
   private readonly PaymentsQueue _paymentQueue;
   private readonly IStorageService _storageService;
   private readonly PaymentProcessorsHealth _ppHealth;
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
      PaymentProcessorsHealth ppHealth,
      [FromKeyedServices("Default")] PaymentProcessorService defaultPaymentProcessor,
      [FromKeyedServices("Fallback")] PaymentProcessorService fallbackPaymentProcessor)
   {
      _logger = logger;
      _paymentQueue = paymentQueue;
      _ppHealth = ppHealth;
      _storageService = storageService;
      _defaultPaymentProcessor = defaultPaymentProcessor;
      _fallbackPaymentProcessor = fallbackPaymentProcessor;

      _startFresh = configuration.GetValue("StartFresh", false);
      _workerLoopCount = configuration.GetValue("NumOfProcessingTaks", 8);
      _percentageOfMostHealthy = configuration.GetValue("PercentageOfMostHealthy", 30);

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
         _logger.LogInformation("Starting fresh");
         await _defaultPaymentProcessor.PurgePayments(stoppingToken);
         await _fallbackPaymentProcessor.PurgePayments(stoppingToken);
         await _storageService.ClearAllTransactions();
      }

      _logger.LogInformation("Starting {WorkerLoopCount} processing loops", _workerLoopCount);

      var workers = Enumerable
         .Range(0, _workerLoopCount)
         .Select(async id =>
         {
            var mode = id < (_percentageOfMostHealthy * _workerLoopCount / 100) ? TaskMode.MostHealthy : TaskMode.DefaultOnly;
            await ProcessingLoop(id, mode, stoppingToken);
         })
         .ToArray();

      await Task.WhenAll(workers);
      _logger.LogInformation("All processing loops ended");
   }

   private async Task ProcessingLoop(int id, TaskMode mode, CancellationToken cancellationToken)
   {
      _logger.LogInformation("Started loop ID={Id} in Mode={Mode}", id, mode);

      if (mode == TaskMode.MostHealthy)
      {
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
      }
      else //if (mode == TaskMode.DefaultOnly)
      {
         var random = new Random();
         while (await _paymentQueue.Reader.WaitToReadAsync(cancellationToken))
         {
            if(_ppHealth.HealthData.DefaultFailing)
            {  // Default is failing. No point in reading anything
               await Task.Delay(random.Next(100, 200), cancellationToken);
               continue;
            }

            while (_paymentQueue.Reader.TryRead(out PaymentRequest request))
            {
               var res = await ProcessPaymentOnDefault(request, cancellationToken);
               if (!res)
               {  // Error processing payment, so enqueue it back and break to check if Default is not failing
                  await _paymentQueue.Enqueue(request);
                  break;
               }
            }
         }
      }

      _logger.LogInformation("Ended loop ID={Id}", id);
   }

   private async Task<bool> ProcessPaymentOnDefault(PaymentRequest paymentRequest, CancellationToken cancellationToken)
   {
      var ppReq = new PaymentProcessorRequest(paymentRequest.CorrelationId, paymentRequest.Amount, DateTimeOffset.UtcNow);
      return await ProcessPaymentOnProcessor(PaymentProcessor.Default, _defaultPaymentProcessor, ppReq, _defaultProcessedPaymentsCounter, _defaultResponseTime, cancellationToken);
   }

   private async Task<bool> ProcessPayment(PaymentRequest paymentRequest, CancellationToken cancellationToken)
   {
      var ppReq = new PaymentProcessorRequest(paymentRequest.CorrelationId, paymentRequest.Amount, DateTimeOffset.UtcNow);

      var pp = _ppHealth.GetPreferedPaymentProcessor();
      if (pp == PaymentProcessor.Default)
      {
         return await ProcessPaymentOnProcessor(pp, _defaultPaymentProcessor, ppReq, _defaultProcessedPaymentsCounter, _defaultResponseTime, cancellationToken);
      }
      else if (pp == PaymentProcessor.Fallback)
      {
         return await ProcessPaymentOnProcessor(pp, _fallbackPaymentProcessor, ppReq, _fallbackProcessedPaymentsCounter, _fallbackResponseTime, cancellationToken);
      }

      throw new ApplicationException("Invalid payment processor delected");
   }

   private async Task<bool> ProcessPaymentOnProcessor(
      PaymentProcessor processor,
      PaymentProcessorService processorService,
      PaymentProcessorRequest request,
      Counter<int> processorCounter,
      Histogram<long> processorRespTime,
      CancellationToken cancellationToken)
   {
      var sw = Stopwatch.StartNew();
      var ret = await processorService.SendPayment(request, cancellationToken);
      sw.Stop();

      if (ret)
      {
         processorCounter.Add(1);
         processorRespTime.Record(sw.ElapsedMilliseconds);
         await _storageService.AddTransaction(request.RequestedAt, request.CorrelationId, request.Amount, processor);
      }
      else
      {
         _errorsCounter.Add(1);
      }

      return ret;
   }
}
