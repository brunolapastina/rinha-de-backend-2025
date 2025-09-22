using System.Text.Json.Serialization;

namespace PaymentGateway;

public enum PaymentProcessor{ None, Default, Fallback }

public readonly record struct PaymentRequest(string CorrelationId, decimal Amount);
public readonly record struct PaymentProcessorRequest(string CorrelationId, decimal Ammount, DateTimeOffset RequestedAt);
public readonly record struct ServiceHealthResponse(bool Failing, int MinResponseTime);
public readonly record struct PaymentSummaryResponse(PaymentSummaryData Default, PaymentSummaryData Fallback);
public readonly record struct PaymentSummaryData(int TotalRequests, decimal TotalAmount );
public readonly record struct PaymentStorage(string CorrelationId, decimal Ammount, PaymentProcessor PaymentProcessor);


[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentProcessorRequest))]
[JsonSerializable(typeof(ServiceHealthResponse))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(PaymentSummaryData))]
[JsonSerializable(typeof(PaymentStorage))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}