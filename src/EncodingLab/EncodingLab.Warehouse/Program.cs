using EncodingLab.Contracts;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var delay = args.Contains("--delay") ? int.Parse(args[Array.IndexOf(args, "--delay") + 1]) : 0;
var verbose = args.Contains("--verbose");
var stock = new Dictionary<string, int>();
var processed = 0;

await using var connection = await new ConnectionFactory { HostName = "localhost" }.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();
await channel.QueueDeclareAsync("orders.created", true, false, false);
await channel.QueueDeclareAsync("orders.processed", true, false, false);
await channel.BasicQosAsync(0, 50, false);

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, ea) =>
{
    var message = OrderCreated.Parser.ParseFrom(ea.Body.Span);
    var order = message.Order;
    var sameBytes = message.ToByteArray().AsSpan().SequenceEqual(ea.Body.Span);

    var ok = order.OrderItems.All(i => stock.GetValueOrDefault(i.ProductId, 1000) >= i.Quantity);
    if (ok)
        foreach (var i in order.OrderItems)
            stock[i.ProductId] = stock.GetValueOrDefault(i.ProductId, 1000) - i.Quantity;

    if (delay > 0) await Task.Delay(delay);

    var result = new OrderProcessed
    {
        OrderId = order.Id,
        Status = ok ? OrderStatus.Confirmed : OrderStatus.Denied,
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
    };
    await channel.BasicPublishAsync("", "orders.processed", false, new BasicProperties { Persistent = true }, result.ToByteArray());
    await channel.BasicAckAsync(ea.DeliveryTag, false);

    Console.WriteLine($"{++processed} {order.Id} {result.Status} reencoded-same-bytes={sameBytes}");
    if (verbose) Console.WriteLine(JsonFormatter.Default.Format(order));
};
await channel.BasicConsumeAsync("orders.created", false, consumer);

Console.WriteLine($"Warehouse: delay={delay} pid={Environment.ProcessId}");
await Task.Delay(Timeout.Infinite);
