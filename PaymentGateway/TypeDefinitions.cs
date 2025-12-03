using System.Text.Json.Serialization;

namespace PaymentGateway;

public enum PaymentProcessor { None, Default, Fallback }

public readonly record struct PaymentRequest(string CorrelationId, decimal Amount);
public readonly record struct PaymentProcessorRequest(
    string CorrelationId, 
    decimal Amount, 
    [property: JsonConverter(typeof(DateTimeOffsetConverter))]
    DateTimeOffset RequestedAt);
public readonly record struct ServiceHealthResponse(bool Failing, int MinResponseTime);
public readonly record struct PaymentSummaryResponse(PaymentSummaryData Default, PaymentSummaryData Fallback);
public readonly record struct PaymentSummaryData(int TotalRequests, decimal TotalAmount);
public readonly record struct PaymentStorage(string CorrelationId, decimal Ammount, PaymentProcessor PaymentProcessor);
public sealed record class HealthData()
{
    public DateTime LastUpdate { get; set; } = DateTime.MinValue;
    public bool DefaultFailing { get; set; } = false;
    public int DefaultMinRespTime { get; set; } = 0;
    public bool FallbackFailing { get; set; } = false;
    public int FallbackMinRespTime { get; set; } = 0;
}


[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentProcessorRequest))]
[JsonSerializable(typeof(ServiceHealthResponse))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(PaymentSummaryData))]
[JsonSerializable(typeof(PaymentStorage))]
[JsonSerializable(typeof(HealthData))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}