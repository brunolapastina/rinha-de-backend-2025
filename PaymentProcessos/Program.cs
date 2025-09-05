using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PaymentProcessos;

public class Program
{
    public static void Main(string[] args)
    {
        GCSettings.LatencyMode = GCLatencyMode.LowLatency;

        var builder = WebApplication.CreateSlimBuilder(args);

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;
            serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });

        var app = builder.Build();

        app.Urls.Add("http://0.0.0.0:9999");

        app.MapPost("/payments", (PaymentRequest request) =>
        {
            return TypedResults.Ok();
        });

        app.MapGet("/payments-summary", ([FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
        {
            return Results.Ok(new PaymentSummaryResponse(new PaymentSummaryData(0, 0), new PaymentSummaryData(0, 0)));
        });

        app.Run();
    }
}


public record PaymentRequest(string CorrelationId, decimal Ammount);
public record PaymentSummaryResponse(PaymentSummaryData Default, PaymentSummaryData Fallback);
public record PaymentSummaryData(int TotalRequests, decimal TotalAmount );


[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(PaymentSummaryData))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
