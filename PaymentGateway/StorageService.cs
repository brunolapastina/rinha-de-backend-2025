using System.Text.Json;
using StackExchange.Redis;

namespace PaymentGateway;

public class StorageService : IStorageService
{
   private const string TransactionsKey = "transactions:logs"; // sorted set key
   private const string HealthDataKey = "transactions:logs"; // sorted set key

   private readonly ILogger<StorageService> _logger;
   private readonly IDatabase _db;

   private readonly CommandFlags _transactionFlags;

   public StorageService(ILogger<StorageService> logger, IConfiguration configuration)
   {
      _logger = logger;

      var redisConfig = configuration.GetValue<string?>("RedisConfig", null) ??
         throw new InvalidConfigurationException("Redis configuration has to be set");

      var fireAndForget = configuration.GetValue<bool>("RedisFireAndForget", false);
      _transactionFlags = fireAndForget ? CommandFlags.FireAndForget : CommandFlags.None;

      var redis = ConnectionMultiplexer.Connect(redisConfig);
      _db = redis.GetDatabase();
   }

   public async Task ClearAllTransactions()
   {
      await _db.KeyDeleteAsync(TransactionsKey);
   }

   public async Task AddTransaction(DateTimeOffset RequestedAt, string CorrelationId, decimal Ammount, PaymentProcessor PaymentProcessor)
   {
      var score = RequestedAt.ToUnixTimeMilliseconds();
      var stg = new PaymentStorage(CorrelationId, Ammount, PaymentProcessor);
      string json = JsonSerializer.Serialize(stg, AppJsonSerializerContext.Default.PaymentStorage);

      var ret = await _db.SortedSetAddAsync(TransactionsKey, json, score, _transactionFlags);
      if (!ret)
      {
         _logger.LogError("Error inserting transation into log");
      }
   }

   public async Task<PaymentStorage[]> GetSummary(DateTime? from, DateTime? to)
   {
      double start = from.HasValue ? new DateTimeOffset(from.Value).ToUnixTimeMilliseconds() : 0;
      double stop = to.HasValue ? new DateTimeOffset(to.Value).ToUnixTimeMilliseconds() : double.MaxValue;
      var res = await _db.SortedSetRangeByScoreAsync(TransactionsKey, start, stop);
      var ret = res.Select(x => JsonSerializer.Deserialize(x!, AppJsonSerializerContext.Default.PaymentStorage)).ToArray();

      _logger.LogTrace("Got {PreCount} - {PostCount} entries from DB from Start:{Start} Stop:{Stop}", res.Length, ret.Length, start, stop);

      return ret;
   }

   public async Task SetHealthData(HealthData healthData)
   {
      string json = JsonSerializer.Serialize(healthData, AppJsonSerializerContext.Default.HealthData);
      var ret = await _db.StringSetAsync(HealthDataKey, json);
      if (!ret)
      {
         _logger.LogError("Error setting health data");
      }
   }

   public async Task<HealthData?> GetHealthData()
   {
      var val = await _db.StringGetAsync(HealthDataKey);
      return val.IsNullOrEmpty ? 
         null :
         JsonSerializer.Deserialize(val!, AppJsonSerializerContext.Default.HealthData);
   }
}
