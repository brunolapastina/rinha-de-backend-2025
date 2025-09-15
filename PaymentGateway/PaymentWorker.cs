using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentGateway;

public class PaymentWorker : BackgroundService
{
   private readonly ILogger<PaymentWorker> _logger;
   private readonly int _workerLoopCount;
   private readonly PaymentsQueue _paymentQueue;
   private readonly PaymentProcessorService _defaultPaymentProcessor;
   private readonly PaymentProcessorService _fallbackPaymentProcessor;

   private readonly CurrentHealth _currentHealth = new();

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
      [FromKeyedServices("Default")] PaymentProcessorService defaultPaymentProcessor,
      [FromKeyedServices("Fallback")] PaymentProcessorService fallbackPaymentProcessor)
   {
      _logger = logger;
      _paymentQueue = paymentQueue;
      _defaultPaymentProcessor = defaultPaymentProcessor;
      _fallbackPaymentProcessor = fallbackPaymentProcessor;

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
      _logger.LogInformation("Starting {WorkerLoopCount} processing loops", _workerLoopCount);

      //var healthCheck = HealthCheckLoop(stoppingToken);

      var workers = Enumerable
         .Range(0, _workerLoopCount)
         .Select(async id => await ProcessingLoop(id, stoppingToken)) // generator
         //.Append(healthCheck)
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
      var best = _currentHealth.GetBestPaymentProcessor();

      _sw.Restart();
      var res = await _defaultPaymentProcessor.SendPayment(paymentRequest, cancellationToken);
      _sw.Stop();
      if (res)
      {
         _defaultProcessedPaymentsCounter.Add(1);
         _defaultResponseTime.Record(_sw.ElapsedMilliseconds);
      }
      else
      {
         _sw.Restart();
         res = await _fallbackPaymentProcessor.SendPayment(paymentRequest, cancellationToken);
         _sw.Stop();
         if (res)
         {
            _fallbackProcessedPaymentsCounter.Add(1);
            _fallbackResponseTime.Record(_sw.ElapsedMilliseconds);
         }
         else
         {
            _errorsCounter.Add(1);
         }
      }      

      return res;
   }

   private async Task HealthCheckLoop(CancellationToken cancellationToken)
   {
      _logger.LogInformation("Started health check loop");

      while (!cancellationToken.IsCancellationRequested)
      {
         var defaultHc = await _defaultPaymentProcessor.GetServiceHealth(cancellationToken);
         if (defaultHc.HasValue)
         {
            _currentHealth.DefaultFailing = defaultHc.Value.Failing;
            _currentHealth.DefaultMinResponseTime = defaultHc.Value.MinResponseTime;
         }

         var fallbackHc = await _fallbackPaymentProcessor.GetServiceHealth(cancellationToken);
         if (fallbackHc.HasValue)
         {
            _currentHealth.FallbackFailing = fallbackHc.Value.Failing;
            _currentHealth.FallbackMinResponseTime = fallbackHc.Value.MinResponseTime;
         }

         _logger.LogInformation("[HealthCheck] Default:[Failing:{defaultFailing}, MinResponseTime:{defaultMinResponseTime}] Fallback:[Failing:{fallbackFailing}, MinResponseTime:{fallbackMinResponseTime}]",
         _currentHealth.DefaultFailing, _currentHealth.DefaultMinResponseTime, _currentHealth.FallbackFailing, _currentHealth.FallbackMinResponseTime);

         await Task.Delay(5000, cancellationToken);
      }

      _logger.LogInformation("Ended health check loop");
   }
}
