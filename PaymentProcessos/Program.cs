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
            return Results.Ok(new PaymentSummaryResponse());
        });

        app.Run();
    }
}

public record PaymentRequest(string CorrelationId, decimal Ammount);

public record PaymentSummaryResponse
{
    //[JsonPropertyName("default")]
    public PaymentSummaryData Default { get; set; } = new PaymentSummaryData();

    //[JsonPropertyName("fallback")]
    public PaymentSummaryData Fallback { get; set; } = new PaymentSummaryData();
}

public record PaymentSummaryData
{
    //[JsonPropertyName("totalRequests")]
    public int TotalRequests { get; set; }

    //[JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }
}


[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(PaymentSummaryData))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
