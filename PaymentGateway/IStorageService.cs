namespace PaymentGateway;

public interface IStorageService
{
   Task AddTransaction(DateTimeOffset RequestedAt, string CorrelationId, decimal Ammount, PaymentProcessor PaymentProcessor);
   Task ClearAllTransactions();
   Task<PaymentStorage[]> GetSummary(DateTime? from, DateTime? to);
   Task SetHealthData(HealthData healthData);
   Task<HealthData?> GetHealthData();
}
