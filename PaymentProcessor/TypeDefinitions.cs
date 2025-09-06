using System.Text.Json.Serialization;

namespace PaymentProcessor;

public readonly record struct PaymentRequest(string CorrelationId, decimal Ammount);
public record PaymentSummaryResponse(PaymentSummaryData Default, PaymentSummaryData Fallback);
public record PaymentSummaryData(int TotalRequests, decimal TotalAmount );


[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(PaymentSummaryData))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}