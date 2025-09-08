using System.Net;
using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PaymentProcessor;

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
                // Recycle connections periodically to avoid stale DNS entries
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),

                // Drop idle connections (frees sockets under high load)
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

                // Controls parallelism per destination
                // Raise if you’re hitting throttling under high load
                MaxConnectionsPerServer = 64, 

                // True = faster reuse of sockets across DNS changes,
                // but stale DNS info may persist if you don’t set a lifetime
                EnableMultipleHttp2Connections = true,

                // Disables automatic decompression if you don’t need it
                AutomaticDecompression = DecompressionMethods.None,

                // For high performance, disable proxy unless you need it
                UseProxy = false,

                // If you don’t need cookies, turn them off
                UseCookies = false
            });

        builder.Services.AddKeyedSingleton("Default", (sp, key) =>
            ActivatorUtilities.CreateInstance<PaymentProcessorService>(sp, (key as string)!));

        builder.Services.AddKeyedSingleton("Fallback", (sp, key) =>
            ActivatorUtilities.CreateInstance<PaymentProcessorService>(sp, (key as string)!));

        builder.Services.AddSingleton<PaymentsQueue>();
        builder.Services.AddHostedService<PaymentWorker>();

        var app = builder.Build();

        app.Urls.Add("http://0.0.0.0:9999");

        app.MapPost("/payments", async (PaymentRequest request, PaymentsQueue paymentQueue) =>
        {
            await paymentQueue.Enqueue(request);
            return TypedResults.Ok();
        });

        app.MapGet("/payments-summary", ([FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
        {
            return Results.Ok(new PaymentSummaryResponse(new PaymentSummaryData(0, 0), new PaymentSummaryData(0, 0)));
        });

        app.Run();
    }
}
