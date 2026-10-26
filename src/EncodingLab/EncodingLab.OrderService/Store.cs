using System.Collections.Concurrent;
using System.Text.Json;
using EncodingLab.Contracts;
using RabbitMQ.Client;

public static class Store
{
    public static readonly ConcurrentDictionary<string, Order> Orders = new();
    public static readonly ConcurrentDictionary<string, Lazy<Task<Order>>> Requests = new();
    public static readonly ConcurrentDictionary<string, byte> Processed = new();
    public static readonly Dictionary<string, int> Catalog =
        JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FindCatalog()))!;
    public static readonly SemaphoreSlim PublishLock = new(1, 1);
    public static IChannel Channel = null!;
    public static int Counter;
    public static int Duplicates;
    public static int DelayMs;
    public static bool Idempotency;
    public static bool Verbose;

    private static string FindCatalog()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (!File.Exists(Path.Combine(dir.FullName, "data", "catalog.json"))) dir = dir.Parent!;
        return Path.Combine(dir.FullName, "data", "catalog.json");
    }
}
