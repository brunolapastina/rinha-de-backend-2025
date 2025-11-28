using System;

namespace PaymentGateway;

public static class HealthCheckExtensions
{
   public static IServiceCollection AddPaymentProcessorsHealthCheck(this IServiceCollection services)
   {
      var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
      var healthCheckMode = config.GetValue<HealthCheckMode?>("HealthCheckMode", null) ??
         throw new InvalidConfigurationException("Health check mode has to be set to either Server or Client");

      if(healthCheckMode == HealthCheckMode.Server)
      {
         services.AddSingleton<PaymentProcessorHealthServer>();
         services.AddSingleton<IPaymentProcessorsHealth>(sp => sp.GetRequiredService<PaymentProcessorHealthServer>());
         services.AddHostedService<HealthCheckWorker>();
      }
      else
      {
         
      }
      
      return services;
   }

   public enum HealthCheckMode
   {
      Server,
      Client
   }
}