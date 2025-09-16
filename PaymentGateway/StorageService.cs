using System;
using System.Collections;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using StackExchange.Redis;

namespace PaymentGateway;

public class StorageService
{
   private const string Key = "transactions:logs"; // sorted set key

   private readonly ILogger<StorageService> _logger;
   private readonly IDatabase _db;

   public StorageService(ILogger<StorageService> logger, IConfiguration configuration)
   {
      _logger = logger;

      var redisConfig = configuration.GetValue<string?>("RedisConfig", null) ??
         throw new InvalidConfigurationException("Redis configuration has to be set");

      var redis = ConnectionMultiplexer.Connect(redisConfig);
      _db = redis.GetDatabase();
   }

   public async Task ClearAllTransactions()
   {
      await _db.KeyDeleteAsync(Key);
   }

   public void AddTransaction(DateTimeOffset RequestedAt, string CorrelationId, decimal Ammount, PaymentProcessor PaymentProcessor)
   {
      var score = RequestedAt.ToUnixTimeMilliseconds();
      var stg = new PaymentStorage(CorrelationId, Ammount, PaymentProcessor);
      string json = JsonSerializer.Serialize(stg, AppJsonSerializerContext.Default.PaymentStorage);

      _logger.LogTrace("Adding transaction - RequestedAt:{Timestamp} Score:{Score} Content:{Content}",
         RequestedAt, score, json);

      var ret = _db.SortedSetAdd(Key, json, score);
      if (!ret)
      {
         _logger.LogError("Error inserting transation into log");
      }
   }

   public async Task<PaymentStorage[]> GetSummary(DateTime? from, DateTime? to)
   {
      double start = from.HasValue ? new DateTimeOffset(from.Value).ToUnixTimeMilliseconds() : 0;
      double stop = to.HasValue ? new DateTimeOffset(to.Value).ToUnixTimeMilliseconds() : double.MaxValue;
      var res = await _db.SortedSetRangeByScoreAsync(Key, start, stop);
      var ret = res.Select(x => JsonSerializer.Deserialize(x!, AppJsonSerializerContext.Default.PaymentStorage)).ToArray();

      _logger.LogTrace("Got {PreCount} - {PostCount} entries from DB from Start:{Start} Stop:{Stop}", res.Length, ret.Length, start, stop);

      return ret;
   }
}
