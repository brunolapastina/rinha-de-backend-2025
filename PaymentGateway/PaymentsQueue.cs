using System.Diagnostics.Metrics;
using System.Threading.Channels;

namespace PaymentProcessor;

public class PaymentsQueue
{
   private readonly Channel<PaymentRequest> _channel = Channel.CreateUnbounded<PaymentRequest>(
      new UnboundedChannelOptions()
      {
         SingleWriter = false,
         SingleReader = false,
         AllowSynchronousContinuations = false
      });

   public ChannelReader<PaymentRequest> Reader { get => _channel.Reader; }

   public PaymentsQueue(IMeterFactory meterFactory)
   {
      var meter = meterFactory.Create("PaymentGateway");
      meter.CreateObservableGauge(
            name: "pending_payments",
            observeValue: () => new Measurement<int>(Reader.Count),
            unit: "payments",
            description: "Number of payments queued, waiting to be processed"
        );
   }

   public ValueTask Enqueue(PaymentRequest request)
   {
      return _channel.Writer.WriteAsync(request);
   }
}
