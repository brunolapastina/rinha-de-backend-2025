using System.Threading.Channels;

namespace PaymentProcessos;

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

   public ValueTask Enqueue(PaymentRequest request)
   {
      return _channel.Writer.WriteAsync(request);
   }
}
