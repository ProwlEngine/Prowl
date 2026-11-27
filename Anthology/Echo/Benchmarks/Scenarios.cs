namespace Benchmarks;

public record Scenario(string Name, Type Type, object Value, bool FileFormats = false);

public static class Scenarios
{
    public static List<Scenario> All()
    {
        var random = new Random(42);
        return new List<Scenario>
        {
            new("Small", typeof(Small), new Small { A = 1, B = 2.5f, C = "hi" }),
            new("Graph.Reflection", typeof(RComplexData), CreateReflectionGraph(random), FileFormats: true),
            new("Graph.SourceGen", typeof(ComplexData), CreateSourceGenGraph(new Random(42))),
            new("Graph.FixedReflection", typeof(List<FPerson>), CreateFixedPeople(random)),
            new("Primitives", typeof(List<AllPrimitives>), CreatePrimitives(random), FileFormats: true),
            new("FloatArray", typeof(float[]), Enumerable.Range(0, 10000).Select(_ => (float)random.NextDouble()).ToArray(), FileFormats: true),
            new("IntList", typeof(List<int>), Enumerable.Range(0, 10000).Select(_ => random.Next()).ToList()),
            new("Structs", typeof(Transform), CreateTransform(random)),
            new("Wide", typeof(List<Wide>), Enumerable.Range(0, 200).Select(i => CreateWide(i)).ToList()),
            new("Deep", typeof(Node), CreateChain(200)),
            new("Polymorphic", typeof(Scene), CreateScene(random)),
            new("ObjectFields", typeof(Bag), CreateBag(random)),
            new("SharedRefs", typeof(Graph), CreateGraph(random)),
            new("Dictionaries", typeof(Inventory), CreateInventory(random)),
            new("Enums", typeof(List<Item>), CreateItems(random)),
            new("SerializeIf", typeof(List<Conditional>), Enumerable.Range(0, 1000).Select(i => new Conditional { HasExtra = i % 3 == 0, Always = i, Extra = "extra" + i, Computed = i * 0.5f }).ToList()),
            new("Strings", typeof(TextBlob), CreateText(random), FileFormats: true),
        };
    }

    public static RComplexData CreateReflectionGraph(Random random) => new()
    {
        Id = new Guid(42, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
        Timestamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
        Coordinates = Enumerable.Range(0, 100).Select(_ => random.NextDouble() * 100).ToArray(),
        Scores = Enumerable.Range(0, 50).ToDictionary(i => $"Player{i}", _ => random.Next(0, 1000)),
        People = Enumerable.Range(0, 100).Select(i => new RPerson
        {
            Name = $"Person {i}",
            Age = random.Next(18, 80),
            Email = $"person{i}@example.com",
            Address = new RAddress { Street = $"{random.Next(1, 9999)} Main St", City = "TestCity", State = "TS", ZipCode = $"{random.Next(10000, 99999)}" },
            Hobbies = Enumerable.Range(0, random.Next(2, 6)).Select(j => $"Hobby {j}").ToList()
        }).ToList()
    };

    public static ComplexData CreateSourceGenGraph(Random random) => new()
    {
        Id = new Guid(42, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10),
        Timestamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
        Coordinates = Enumerable.Range(0, 100).Select(_ => random.NextDouble() * 100).ToArray(),
        Scores = Enumerable.Range(0, 50).ToDictionary(i => $"Player{i}", _ => random.Next(0, 1000)),
        People = Enumerable.Range(0, 100).Select(i => new Person
        {
            Name = $"Person {i}",
            Age = random.Next(18, 80),
            Email = $"person{i}@example.com",
            Address = new Address { Street = $"{random.Next(1, 9999)} Main St", City = "TestCity", State = "TS", ZipCode = $"{random.Next(10000, 99999)}" },
            Hobbies = Enumerable.Range(0, random.Next(2, 6)).Select(j => $"Hobby {j}").ToList()
        }).ToList()
    };

    private static List<FPerson> CreateFixedPeople(Random random) =>
        Enumerable.Range(0, 300).Select(i => new FPerson
        {
            Name = $"Person {i}",
            Age = random.Next(18, 80),
            Email = $"person{i}@example.com",
            Hobbies = new List<string> { "a", "b" }
        }).ToList();

    private static List<AllPrimitives> CreatePrimitives(Random random) =>
        Enumerable.Range(0, 1000).Select(i => new AllPrimitives
        {
            B = (byte)i, SB = (sbyte)(i % 100), S = (short)i, US = (ushort)i, I = random.Next(), UI = (uint)random.Next(),
            L = random.NextInt64(), UL = (ulong)random.NextInt64(), F = (float)random.NextDouble(), D = random.NextDouble(),
            M = i * 1.25m, Bo = i % 2 == 0, C = (char)('a' + i % 26), Str = "s" + i
        }).ToList();

    private static Transform CreateTransform(Random random) => new()
    {
        Position = new Vec3 { X = 1, Y = 2, Z = 3 },
        Scale = new Vec3 { X = 1, Y = 1, Z = 1 },
        Points = Enumerable.Range(0, 5000).Select(_ => new Vec3 { X = (float)random.NextDouble(), Y = (float)random.NextDouble(), Z = (float)random.NextDouble() }).ToArray()
    };

    private static Wide CreateWide(int i)
    {
        var w = new Wide();
        foreach (var f in typeof(Wide).GetFields())
        {
            if (f.FieldType == typeof(int)) f.SetValue(w, i);
            else if (f.FieldType == typeof(float)) f.SetValue(w, i * 0.5f);
            else if (f.FieldType == typeof(string)) f.SetValue(w, f.Name + i);
            else if (f.FieldType == typeof(bool)) f.SetValue(w, i % 2 == 0);
        }
        return w;
    }

    private static Node CreateChain(int depth)
    {
        Node head = new() { Value = 0 };
        Node current = head;
        for (int i = 1; i < depth; i++)
        {
            current.Next = new Node { Value = i };
            current = current.Next;
        }
        return head;
    }

    private static Scene CreateScene(Random random) => new()
    {
        Shapes = Enumerable.Range(0, 1000).Select<int, Shape>(i => (i % 3) switch
        {
            0 => new Circle { Name = "c" + i, Radius = (float)random.NextDouble() },
            1 => new Rect { Name = "r" + i, Width = 1, Height = 2 },
            _ => new Polygon { Name = "p" + i, Points = new List<Vec3> { new() { X = 1 }, new() { Y = 1 } } }
        }).ToList()
    };

    private static Bag CreateBag(Random random) => new()
    {
        Items = Enumerable.Range(0, 1000).Select<int, object?>(i => (i % 5) switch
        {
            0 => i,
            1 => (float)random.NextDouble(),
            2 => "str" + i,
            3 => null,
            _ => new Small { A = i }
        }).ToArray()
    };

    private static Graph CreateGraph(Random random)
    {
        var nodes = Enumerable.Range(0, 500).Select(i => new GraphNode { Id = i }).ToList();
        foreach (var node in nodes)
        {
            node.Parent = nodes[random.Next(nodes.Count)];
            for (int i = 0; i < 3; i++)
                node.Links.Add(nodes[random.Next(nodes.Count)]);
        }
        return new Graph { Nodes = nodes };
    }

    private static Inventory CreateInventory(Random random) => new()
    {
        Counts = Enumerable.Range(0, 1000).ToDictionary(i => "item" + i, _ => random.Next(100)),
        Owners = Enumerable.Range(0, 200).ToDictionary(i => i, i => new RPerson { Name = "Owner" + i, Age = i })
    };

    private static List<Item> CreateItems(Random random) =>
        Enumerable.Range(0, 1000).Select(i => new Item
        {
            Rarity = (Rarity)(i % 5),
            Tags = (Tags)(i % 16),
            Maybe = i % 2 == 0 ? (Rarity)(i % 5) : null,
            Count = i
        }).ToList();

    private static TextBlob CreateText(Random random) => new()
    {
        Ascii = string.Concat(Enumerable.Range(0, 2000).Select(i => "lorem ipsum dolor sit amet ")),
        Unicode = string.Concat(Enumerable.Range(0, 500).Select(i => "héllo wörld 你好 ")),
        Lines = Enumerable.Range(0, 1000).Select(i => $"line number {i} with some text").ToList()
    };
}
