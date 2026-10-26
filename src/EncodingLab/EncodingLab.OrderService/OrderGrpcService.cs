using EncodingLab.Contracts;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using RabbitMQ.Client;

public class OrderGrpcService : OrderService.OrderServiceBase
{
    public override async Task<CreateOrderResponse> CreateOrder(CreateOrderRequest request, ServerCallContext context)
    {
        if (Store.Verbose) Console.WriteLine($"CreateOrder: {JsonFormatter.Default.Format(request)}");

        var order = Store.Idempotency
            ? await Store.Requests.GetOrAdd(request.RequestId, _ => new Lazy<Task<Order>>(() => Create(request))).Value
            : await Create(request);

        return new CreateOrderResponse { Order = order };
    }

    public override Task<GetOrderResponse> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        if (!Store.Orders.TryGetValue(request.OrderId, out var order))
            throw new RpcException(new Status(StatusCode.NotFound, $"order {request.OrderId} not found"));

        return Task.FromResult(new GetOrderResponse { Order = order });
    }

    public override Task<GetCustomerOrdersResponse> GetCustomerOrders(GetCustomerOrdersRequest request, ServerCallContext context)
    {
        var response = new GetCustomerOrdersResponse();
        response.Orders.AddRange(Store.Orders.Values.Where(o => o.CustomerId == request.CustomerId));
        return Task.FromResult(response);
    }

    private static async Task<Order> Create(CreateOrderRequest request)
    {
        var order = request.Order.Clone();
        order.Id = $"S{Interlocked.Increment(ref Store.Counter):D6}";
        order.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        order.Status = OrderStatus.Pending;
        foreach (var item in order.OrderItems)
            item.PricePerUnit = Store.Catalog.GetValueOrDefault(item.ProductId);

        Store.Orders[order.Id] = order;

        await Store.PublishLock.WaitAsync();
        try
        {
            var body = new OrderCreated { Order = order }.ToByteArray();
            await Store.Channel.BasicPublishAsync("", "orders.created", false, new BasicProperties { Persistent = true }, body);
        }
        finally
        {
            Store.PublishLock.Release();
        }

        if (Store.DelayMs > 0) await Task.Delay(Store.DelayMs);
        return order;
    }
}
