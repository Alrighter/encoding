using EncodingLab.Contracts;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

// dotnet run --project EncodingLab.Tools -- gen
switch (args.FirstOrDefault())
{
    case "gen": Generate(seed: 100, count: 10_000, dataDir: "../../data"); break;
    case "bench": Bench(dataDir: "../../data", runs: 5); break;
    default: Console.WriteLine("usage: gen | bench"); break;
}

static void Generate(int seed, int count, string dataDir)
{
    var rnd = new Random(seed);
    string[] cities = { "Київ", "Львів", "Одеса", "Харків", "Дніпро" };
    string[] comments = { "Подзвоніть заздалегідь", "Залиште біля дверей", "Доставка після 18:00" };

    var catalog = Enumerable.Range(1, 500).ToDictionary(p => $"P{p:D3}", _ => rnd.Next(1_000, 500_000));
    var productIds = catalog.Keys.ToArray();

    var orders = new List<string>();
    for (int i = 1; i <= count; i++)
    {
        var order = new Order
        {
            Id = $"O{i:D5}",
            CustomerId = $"C{rnd.Next(1, 1001):D4}",
            CreatedAt = Timestamp.FromDateTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rnd.Next(400_000))),
            Status = OrderStatus.Pending,
            Address = new Address { City = cities[rnd.Next(cities.Length)], Street = $"вул. Шевченка, {rnd.Next(1, 200)}", PostalCode = $"{rnd.Next(1, 100_000):D5}" },
        };

        int items = rnd.Next(1, 21);
        for (int k = 0; k < items; k++)
        {
            var productId = productIds[rnd.Next(productIds.Length)];
            order.OrderItems.Add(new OrderItem { ProductId = productId, Quantity = rnd.Next(1, 11), PricePerUnit = catalog[productId] });
        }

        if (rnd.Next(2) == 0) 
            order.Comment = comments[rnd.Next(comments.Length)];

        orders.Add(JsonFormatter.Default.Format(order));
    }

    Directory.CreateDirectory(dataDir);
    File.WriteAllText(Path.Combine(dataDir, "catalog.json"), JsonSerializer.Serialize(catalog));
    File.WriteAllText(Path.Combine(dataDir, "orders.json"), "[\n" + string.Join(",\n", orders) + "\n]");
    Console.WriteLine($"{count} замовлень -> {Path.GetFullPath(dataDir)}");
}

// Експеримент 1a: ті самі 10 000 замовлень у JSON і Protobuf — розмір, час кодування, час декодування.
static void Bench(string dataDir, int runs)
{
    var orders = File.ReadLines(Path.Combine(dataDir, "orders.json"))
        .Where(l => l.StartsWith("{"))
        .Select(l => Order.Parser.ParseJson(l.TrimEnd(',')))
        .ToList();

    var results = new List<(long jsonSize, long protoSize, double jsonEnc, double jsonDec, double protoEnc, double protoDec)>();
    for (int r = 0; r <= runs; r++)
    {
        var sw = Stopwatch.StartNew();
        var json = orders.Select(o => Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(o))).ToList();
        double jsonEnc = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        foreach (var b in json) Order.Parser.ParseJson(Encoding.UTF8.GetString(b));
        double jsonDec = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var proto = orders.Select(o => o.ToByteArray()).ToList();
        double protoEnc = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        foreach (var b in proto) Order.Parser.ParseFrom(b);
        double protoDec = sw.Elapsed.TotalMilliseconds;

        if (r > 0) results.Add((json.Sum(b => (long)b.Length), proto.Sum(b => (long)b.Length), jsonEnc, jsonDec, protoEnc, protoDec));
    }

    double Median(Func<(long, long, double, double, double, double), double> f) =>
        results.Select(x => f(x)).OrderBy(x => x).ElementAt(results.Count / 2);

    var (jsonSize, protoSize, _, _, _, _) = results[0];
    Console.WriteLine($"{orders.Count} замовлень, {runs} запусків + прогрів, медіана");
    Console.WriteLine($"{"",-10}{"розмір, байт",15}{"кодування, мс",16}{"декодування, мс",18}");
    Console.WriteLine($"{"JSON",-10}{jsonSize,15:N0}{Median(x => x.Item3),16:F1}{Median(x => x.Item4),18:F1}");
    Console.WriteLine($"{"Protobuf",-10}{protoSize,15:N0}{Median(x => x.Item5),16:F1}{Median(x => x.Item6),18:F1}");
    Console.WriteLine($"Protobuf компактніший у {(double)jsonSize / protoSize:F2} раза");

    // Для питання 2 звіту: найменше замовлення — JSON і hex-дамп Protobuf
    var smallest = orders.MinBy(o => o.CalculateSize())!;
    Console.WriteLine($"\nНайменше замовлення ({smallest.CalculateSize()} байт Protobuf):\n{JsonFormatter.Default.Format(smallest)}");
    Console.WriteLine(Convert.ToHexString(smallest.ToByteArray()));
}
