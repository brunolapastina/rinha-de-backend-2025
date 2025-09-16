using System.Net;
using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PaymentGateway;

public class Program
{
    public static void Main(string[] args)
    {
        GCSettings.LatencyMode = GCLatencyMode.LowLatency;

        var builder = WebApplication.CreateSlimBuilder(args);

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

        builder.Services
            .AddHttpClient("PaymentProcessor")
            .ConfigurePrimaryHttpMessageHandler(() =>
            new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),     // Recycle connections periodically to avoid stale DNS entries
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),  // Drop idle connections (frees sockets under high load)
                MaxConnectionsPerServer = 64,                           // Controls parallelism per destination
                EnableMultipleHttp2Connections = true,                  // True = faster reuse of sockets across DNS changes, but stale DNS info may persist if you don’t set a lifetime
                AutomaticDecompression = DecompressionMethods.None,     // Disables automatic decompression if you don’t need it
                UseProxy = false,                                       // For high performance, disable proxy unless you need it
                UseCookies = false                                      // Don't need cookies, so turn it off
            });

        builder.Services.AddKeyedSingleton("Default", (sp, key) =>
            ActivatorUtilities.CreateInstance<PaymentProcessorService>(sp, (key as string)!));

        builder.Services.AddKeyedSingleton("Fallback", (sp, key) =>
            ActivatorUtilities.CreateInstance<PaymentProcessorService>(sp, (key as string)!));

        builder.Services.AddSingleton<StorageService>();
        builder.Services.AddSingleton<PaymentsQueue>();
        builder.Services.AddHostedService<PaymentWorker>();

        var app = builder.Build();

        app.Urls.Add("http://0.0.0.0:9999");

        app.MapPost("/payments", async (PaymentRequest request, PaymentsQueue paymentQueue) =>
        {
            await paymentQueue.Enqueue(request);
            return TypedResults.Ok();
        });

        app.MapGet("/payments-summary", async ([FromQuery] DateTime? from, [FromQuery] DateTime? to, StorageService storageService) =>
        {
            var res = await storageService.GetSummary(from, to);
            var grp = res.GroupBy(x => x.PaymentProcessor);
            return Results.Ok(new PaymentSummaryResponse(
                new PaymentSummaryData(
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Default)?.Count() ?? 0,
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Default)?.Sum(x => x.Ammount) ?? 0),
                new PaymentSummaryData(
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Fallback)?.Count() ?? 0,
                    grp.FirstOrDefault(x => x.Key == PaymentProcessor.Fallback)?.Sum(x => x.Ammount) ?? 0)));
        });

        app.Run();
    }
}
