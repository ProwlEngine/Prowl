using System.Text.Json;
using MessagePack;
using Newtonsoft.Json;
using Prowl.Echo;

namespace Benchmarks;

[MessagePackObject]
public class MsgPackPerson
{
    [Key(0)] public string? Name;
    [Key(1)] public int Age;
    [Key(2)] public string? Email;
    [Key(3)] public MsgPackAddress? Address;
    [Key(4)] public List<string>? Hobbies;
}

[MessagePackObject]
public class MsgPackAddress
{
    [Key(0)] public string? Street;
    [Key(1)] public string? City;
    [Key(2)] public string? State;
    [Key(3)] public string? ZipCode;
}

[MessagePackObject]
public class MsgPackComplexData
{
    [Key(0)] public List<MsgPackPerson>? People;
    [Key(1)] public Dictionary<string, int>? Scores;
    [Key(2)] public DateTime Timestamp;
    [Key(3)] public Guid Id;
    [Key(4)] public double[] Coordinates = Array.Empty<double>();
}

public static class Comparison
{
    public static void Run()
    {
        var data = Scenarios.CreateReflectionGraph(new Random(42));
        var generated = Scenarios.CreateSourceGenGraph(new Random(42));
        var msgPack = ToMsgPack(data);
        var stjOptions = new JsonSerializerOptions { IncludeFields = true };

        string stj = System.Text.Json.JsonSerializer.Serialize(data, stjOptions);
        string newtonsoft = JsonConvert.SerializeObject(data);
        byte[] msgPackBytes = MessagePackSerializer.Serialize(msgPack);
        EchoObject echo = Serializer.Serialize(data);
        byte[] echoBinary = new EchoBinaryFormat().WriteToBytes(echo);
        EchoObject echoGenerated = Serializer.Serialize(generated);

        var cases = new (string, string, Action)[]
        {
            ("System.Text.Json", "Serialize", () => System.Text.Json.JsonSerializer.Serialize(data, stjOptions)),
            ("System.Text.Json", "Deserialize", () => System.Text.Json.JsonSerializer.Deserialize<RComplexData>(stj, stjOptions)),
            ("Newtonsoft.Json", "Serialize", () => JsonConvert.SerializeObject(data)),
            ("Newtonsoft.Json", "Deserialize", () => JsonConvert.DeserializeObject<RComplexData>(newtonsoft)),
            ("MessagePack", "Serialize", () => MessagePackSerializer.Serialize(msgPack)),
            ("MessagePack", "Deserialize", () => MessagePackSerializer.Deserialize<MsgPackComplexData>(msgPackBytes)),
            ("Echo", "Serialize", () => Serializer.Serialize(data)),
            ("Echo", "Deserialize", () => Serializer.Deserialize<RComplexData>(echo)),
            ("Echo+Binary", "Serialize", () => new EchoBinaryFormat().WriteToBytes(Serializer.Serialize(data))),
            ("Echo+Binary", "Deserialize", () => Serializer.Deserialize<RComplexData>(new EchoBinaryFormat().ReadFromBytes(echoBinary))),
            ("Echo SourceGen", "Serialize", () => Serializer.Serialize(generated)),
            ("Echo SourceGen", "Deserialize", () => Serializer.Deserialize<ComplexData>(echoGenerated)),
        };

        Harness.PrintHeader(false);
        foreach (var (library, op, action) in cases)
            Harness.Print(Harness.Measure(library, op, action), null);
    }

    private static MsgPackComplexData ToMsgPack(RComplexData data) => new()
    {
        Id = data.Id,
        Timestamp = data.Timestamp,
        Coordinates = data.Coordinates,
        Scores = data.Scores,
        People = data.People?.Select(p => new MsgPackPerson
        {
            Name = p.Name,
            Age = p.Age,
            Email = p.Email,
            Hobbies = p.Hobbies,
            Address = p.Address == null ? null : new MsgPackAddress
            {
                Street = p.Address.Street,
                City = p.Address.City,
                State = p.Address.State,
                ZipCode = p.Address.ZipCode
            }
        }).ToList()
    };
}
