using System.Net;
using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace PaymentGateway;

public partial class Program
{
    protected Program() { }

    public static void Main(string[] args)
    {
        GCSettings.LatencyMode = GCLatencyMode.LowLatency;

        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Logging.ClearProviders();
        builder.Metrics.ClearListeners();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;
            serverOptions.AllowResponseHeaderCompression = false;
            serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });

        ConfigurePaymentProcessor(builder, "Default");
        ConfigurePaymentProcessor(builder, "Fallback");

        builder.Services.AddSingleton<IStorageService, StorageService>();
        builder.Services.AddSingleton<PaymentsQueue>();
        builder.Services.AddSingleton<PaymentProcessorsHealth>();
        builder.Services.AddHostedService<PaymentWorker>();

        var app = builder.Build();

        var appPort = app.Configuration.GetValue("AppPort", 8080);
        app.Urls.Add($"http://0.0.0.0:{appPort}");

        app.MapPost("/payments", async (HttpContext httpContext, PaymentsQueue paymentQueue) =>
        {
            var request = await httpContext.Request.ReadFromJsonAsync(AppJsonSerializerContext.Default.PaymentRequest);
            await paymentQueue.Enqueue(request);
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
        });

        app.MapGet("/payments-summary", async ([FromQuery] DateTime? from, [FromQuery] DateTime? to, HttpContext httpContext, IStorageService storageService) =>
        {
            var res = await storageService.GetSummary(from, to);
            var grp = res.GroupBy(x => x.PaymentProcessor);
            var summary = new PaymentSummaryResponse(
                new PaymentSummaryData(
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Default)?.Count() ?? 0,
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Default)?.Sum(x => x.Ammount) ?? 0),
                new PaymentSummaryData(
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Fallback)?.Count() ?? 0,
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Fallback)?.Sum(x => x.Ammount) ?? 0));

            app.Logger.LogDebug("Sumary requested from {From} to {To} => {Summary}", from, to, summary);

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await httpContext.Response.WriteAsJsonAsync(summary, AppJsonSerializerContext.Default.PaymentSummaryResponse);
        });

        app.MapPost("/purge-payments", async (HttpContext httpContext, IStorageService storageService) =>
        {
            app.Logger.LogInformation("Purging payments");
            await storageService.ClearAllTransactions();
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
        });

        app.Run();
    }

    static private HttpMessageHandler ConfigureMessageHandler() =>
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),    // Recycle connections periodically to avoid stale DNS entries
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(4),  // Drop idle connections (frees sockets under high load)
            MaxConnectionsPerServer = 100,                          // Controls parallelism per destination
            EnableMultipleHttp2Connections = true,                  // True = faster reuse of sockets across DNS changes, but stale DNS info may persist if you don’t set a lifetime
            AutomaticDecompression = DecompressionMethods.None,     // Disables automatic decompression if you don’t need it
            UseProxy = false,                                       // For high performance, disable proxy unless you need it
            UseCookies = false                                      // Don't need cookies, so turn it off
        };

    static private void ConfigureHttpClient(HttpClient client, IServiceProvider sp, string instance)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.ConnectionClose = false;
        client.DefaultRequestHeaders.Add("Connection", "keep-alive");
        client.DefaultRequestHeaders.Add("Keep-Alive", "timeout=30, max=100");

        var config = sp.GetService<IConfiguration>();

        client.BaseAddress = new Uri(config?.GetSection($"PaymentProcessors:{instance}:BaseAddress").Get<string>() ??
         throw new InvalidConfigurationException($"{instance} payment processor does not have a configured Base Address"));
    }

    static private void ConfigurePaymentProcessor(WebApplicationBuilder builder, string instance)
    {
        builder.Services
            .AddHttpClient($"PaymentProcessor:{instance}")
            .ConfigurePrimaryHttpMessageHandler(ConfigureMessageHandler)
            .ConfigureHttpClient((sp, client) => ConfigureHttpClient(client, sp, instance));

        builder.Services.AddKeyedSingleton(instance, (sp, key) =>
            ActivatorUtilities.CreateInstance<PaymentProcessorService>(sp, (key as string)!));
    }

}
