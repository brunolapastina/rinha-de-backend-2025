using System.Text.Json.Serialization;

namespace PaymentGateway;

public enum PaymentProcessor{ None, Default, Fallback }

public record class PaymentRequest(string CorrelationId, decimal Amount);
public readonly record struct PaymentProcessorRequest(string CorrelationId, decimal Ammount, DateTimeOffset RequestedAt);
public readonly record struct PaymentSummaryResponse(PaymentSummaryData Default, PaymentSummaryData Fallback);
public readonly record struct PaymentSummaryData(int TotalRequests, decimal TotalAmount );
public readonly record struct PaymentStorage(string CorrelationId, decimal Ammount, PaymentProcessor PaymentProcessor);


[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentProcessorRequest))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(PaymentSummaryData))]
[JsonSerializable(typeof(PaymentStorage))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}