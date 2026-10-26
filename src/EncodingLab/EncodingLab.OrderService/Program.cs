using EncodingLab.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
var app = builder.Build();
app.MapGrpcService<OrderGrpcService>();

Store.DelayMs = app.Configuration.GetValue("delay", 0);
Store.Idempotency = app.Configuration.GetValue("idempotency", false);
Store.Verbose = app.Configuration.GetValue("verbose", false);
var statsFile = app.Configuration.GetValue<string>("stats");

var connection = await new ConnectionFactory { HostName = "localhost" }.CreateConnectionAsync();
Store.Channel = await connection.CreateChannelAsync();
await Store.Channel.QueueDeclareAsync("orders.created", true, false, false);
await Store.Channel.QueueDeclareAsync("orders.processed", true, false, false);

var consumer = new AsyncEventingBasicConsumer(Store.Channel);
consumer.ReceivedAsync += async (_, ea) =>
{
    var processed = OrderProcessed.Parser.ParseFrom(ea.Body.Span);
    if (Store.Orders.TryGetValue(processed.OrderId, out var order)) order.Status = processed.Status;
    if (!Store.Processed.TryAdd(processed.OrderId, 0)) Interlocked.Increment(ref Store.Duplicates);
    await Store.Channel.BasicAckAsync(ea.DeliveryTag, false);
};
await Store.Channel.BasicConsumeAsync("orders.processed", false, consumer);

_ = Task.Run(async () =>
{
    var last = "";
    while (true)
    {
        await Task.Delay(2000);
        var stats = $"created={Store.Orders.Count} processed={Store.Processed.Count} duplicates={Store.Duplicates}";
        if (stats == last) continue;
        Console.WriteLine(last = stats);
        if (statsFile != null) File.WriteAllText(statsFile, stats);
    }
});

Console.WriteLine($"OrderService: delay={Store.DelayMs} idempotency={Store.Idempotency}");
app.Run();
