using System;
using System.Net.Mime;
using System.Text.Json;

namespace PaymentProcessor;

public class PaymentProcessorService
{
   const string PaymentsEndpoint = "/payments";
   const string ServiceHealthEndpoint = "/payments/service-health";

   private static readonly System.Net.Http.Headers.MediaTypeHeaderValue JsonContetType = new("application/json");
   private readonly HttpClient _client;
   private readonly ILogger<PaymentProcessorService> _logger;
   private readonly string _key;

   public PaymentProcessorService(ILogger<PaymentProcessorService> logger, IHttpClientFactory httpFactory, IConfiguration configuration, string key)
   {
      _logger = logger;
      _client = httpFactory.CreateClient();
      _key = key;

      _client.BaseAddress = new Uri(configuration.GetSection($"PaymentProcessors:{_key}:BaseAddress").Get<string>() ??
         throw new InvalidConfigurationException($"{_key} payment processor does not have a configured Base Address"));

      _logger.LogInformation("{PaymentProcessor} payment processor service created: BaseAddress={BaseAddress}", _key, _client.BaseAddress.ToString());
   }

   public async Task<bool> SendPayment(PaymentRequest request, CancellationToken cancellationToken)
   {
      var ppReq = new PaymentProcessorRequest(request.CorrelationId, request.Ammount, DateTime.UtcNow);

      var jsonContent = JsonSerializer.Serialize(ppReq, AppJsonSerializerContext.Default.PaymentProcessorRequest);

      //TODO: have a memoryPool of StringContent`s so that we decrease the GC pressure
      var content = new StringContent(jsonContent, System.Text.Encoding.UTF8);
      content.Headers.ContentType = JsonContetType;

      var response = await _client.PostAsync(PaymentsEndpoint, content, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
         _logger.LogError("Error on {PaymentProcessor} payment processor. Endppoint={Endpoint} StatusCode={StatusCode} Content={ErrorContent}",
            _key, PaymentsEndpoint, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
         return false;
      }
      
      return true;
   }
   
   public async Task<ServiceHealthResponse?> GetServiceHealth(CancellationToken cancellationToken)
   {
      var response = await _client.GetAsync(ServiceHealthEndpoint, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
         _logger.LogError("Error on {PaymentProcessor} payment processor. Endppoint={Endpoint} StatusCode={StatusCode} Content={ErrorContent}",
            _key, ServiceHealthEndpoint, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
         return null;
      }

      return await response.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.ServiceHealthResponse, cancellationToken);
   }
}
