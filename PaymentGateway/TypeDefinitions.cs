using System.Text.Json.Serialization;

namespace PaymentGateway;

public readonly record struct PaymentRequest(string CorrelationId, decimal Ammount);
public readonly record struct PaymentProcessorRequest(string CorrelationId, decimal Ammount, DateTime RequestedAt);
public readonly record struct ServiceHealthResponse(bool Failing, int MinResponseTime);
public record PaymentSummaryResponse(PaymentSummaryData Default, PaymentSummaryData Fallback);
public record PaymentSummaryData(int TotalRequests, decimal TotalAmount );


[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentProcessorRequest))]
[JsonSerializable(typeof(ServiceHealthResponse))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(PaymentSummaryData))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}