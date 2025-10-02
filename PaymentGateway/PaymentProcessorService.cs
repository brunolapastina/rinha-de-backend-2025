using System.Net;

namespace PaymentGateway;

public class PaymentProcessorService
{
   static private readonly Uri PaymentsEndpoint = new ("/payments", UriKind.Relative);
   static private readonly Uri ServiceHealthEndpoint = new("/payments/service-health", UriKind.Relative);
   static private readonly Uri PurgePaymentsEndpoint = new("/admin/purge-payments", UriKind.Relative);
   static private readonly System.Net.Http.Headers.MediaTypeHeaderValue JsonContetType = new("application/json");

   private readonly HttpClient _client;
   private readonly ILogger<PaymentProcessorService> _logger;
   private readonly string _key;
   private readonly string? _token;

   public PaymentProcessorService(ILogger<PaymentProcessorService> logger, IHttpClientFactory httpFactory, IConfiguration configuration, string key)
   {
      _logger = logger;
      _client = httpFactory.CreateClient($"PaymentProcessor:{_key}");
      _key = key;

      _token = configuration.GetSection($"PaymentProcessors:{_key}:Token").Get<string>();

      _logger.LogInformation("{PaymentProcessor} payment processor service created: BaseAddress={BaseAddress}", _key, _client.BaseAddress?.ToString());
   }

   public async Task<bool> SendPayment(PaymentProcessorRequest ppReq, CancellationToken cancellationToken)
   {
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
         _logger.LogWarning("Error on {PaymentProcessor} payment processor. Endppoint={Endpoint} StatusCode={StatusCode} Content={ErrorContent}",
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
         _logger.LogWarning("Error on {PaymentProcessor} payment processor. Endppoint={Endpoint} StatusCode={StatusCode} Content={ErrorContent}",
            _key, ServiceHealthEndpoint, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
         return null;
      }

      return await response.Content.ReadFromJsonAsync(AppJsonSerializerContext.Default.ServiceHealthResponse, cancellationToken);
   }

   public async Task PurgePayments(CancellationToken cancellationToken)
   {
      using var request = new HttpRequestMessage(HttpMethod.Post, PurgePaymentsEndpoint)
      {
         Version = HttpVersion.Version11,
         VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
         Headers = { { "X-Rinha-Token", _token } }
      };

      using var response = await _client.SendAsync(request, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
         _logger.LogError("Error on {PaymentProcessor} payment processor. Endppoint={Endpoint} StatusCode={StatusCode} Content={ErrorContent}",
            _key, PurgePaymentsEndpoint, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
      }
   }
}
