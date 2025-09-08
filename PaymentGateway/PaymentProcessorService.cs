using System;
using System.Net;
using System.Net.Mime;
using System.Text.Json;

namespace PaymentProcessor;

public class PaymentProcessorService
{
   static private readonly Uri PaymentsEndpoint = new ("/payments", UriKind.Relative);
   static private readonly Uri ServiceHealthEndpoint = new("/payments/service-health", UriKind.Relative);

   private static readonly System.Net.Http.Headers.MediaTypeHeaderValue JsonContetType = new("application/json");
   private readonly HttpClient _client;
   private readonly ILogger<PaymentProcessorService> _logger;
   private readonly string _key;

   public PaymentProcessorService(ILogger<PaymentProcessorService> logger, IHttpClientFactory httpFactory, IConfiguration configuration, string key)
   {
      _logger = logger;
      _client = httpFactory.CreateClient("PaymentProcessor");
      _key = key;

      _client.BaseAddress = new Uri(configuration.GetSection($"PaymentProcessors:{_key}:BaseAddress").Get<string>() ??
         throw new InvalidConfigurationException($"{_key} payment processor does not have a configured Base Address"));

      _logger.LogInformation("{PaymentProcessor} payment processor service created: BaseAddress={BaseAddress}", _key, _client.BaseAddress.ToString());
   }

   public async Task<bool> SendPayment(PaymentRequest paymentRequest, CancellationToken cancellationToken)
   {
      // Skip this object creatioon and serialize data using Utf8JsonWriter
      var ppReq = new PaymentProcessorRequest(paymentRequest.CorrelationId, paymentRequest.Ammount, DateTime.UtcNow);

      //TODO: have a memoryPool of StringContent`s so that we decrease the GC pressure
      using var content = JsonContent.Create(ppReq, AppJsonSerializerContext.Default.PaymentProcessorRequest, JsonContetType);

      using var request = new HttpRequestMessage(HttpMethod.Post, PaymentsEndpoint)
      {
         Version = HttpVersion.Version11,
         VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
         Content = content
      };

      using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
      using var response = await _client.GetAsync(ServiceHealthEndpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
         _logger.LogError("Error on {PaymentProcessor} payment processor. Endppoint={Endpoint} StatusCode={StatusCode} Content={ErrorContent}",
            _key, ServiceHealthEndpoint, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
         return null;
      }

      return await response.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.ServiceHealthResponse, cancellationToken);
   }
}
