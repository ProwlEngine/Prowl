using Prowl.Echo;

namespace Benchmarks;

public class RPerson
{
    public string? Name;
    public int Age;
    public string? Email;
    public RAddress? Address;
    public List<string>? Hobbies;
}

public class RAddress
{
    public string? Street;
    public string? City;
    public string? State;
    public string? ZipCode;
}

public class RComplexData
{
    public List<RPerson>? People;
    public Dictionary<string, int>? Scores;
    public DateTime Timestamp;
    public Guid Id;
    public double[] Coordinates = Array.Empty<double>();
}

[FixedEchoStructure]
public class FPerson
{
    public string? Name;
    public int Age;
    public string? Email;
    public List<string>? Hobbies;
}

[FixedEchoStructure]
[GenerateSerializer]
public partial class Person
{
    public string? Name;
    public int Age;
    public string? Email;
    public Address? Address;
    public List<string>? Hobbies;
}

[FixedEchoStructure]
[GenerateSerializer]
public partial class Address
{
    public string? Street;
    public string? City;
    public string? State;
    public string? ZipCode;
}

[FixedEchoStructure]
[GenerateSerializer]
public partial class ComplexData
{
    public List<Person>? People;
    public Dictionary<string, int>? Scores;
    public DateTime Timestamp;
    public Guid Id;
    public double[] Coordinates = Array.Empty<double>();
}

public class AllPrimitives
{
    public byte B;
    public sbyte SB;
    public short S;
    public ushort US;
    public int I;
    public uint UI;
    public long L;
    public ulong UL;
    public float F;
    public double D;
    public decimal M;
    public bool Bo;
    public char C;
    public string? Str;
}

public struct Vec3
{
    public float X, Y, Z;
}

public class Transform
{
    public Vec3 Position;
    public Vec3 Scale;
    public Vec3[] Points = Array.Empty<Vec3>();
}

public class Wide
{
    public int F00, F01, F02, F03, F04, F05, F06, F07, F08, F09;
    public float G00, G01, G02, G03, G04, G05, G06, G07, G08, G09;
    public string? H00, H01, H02, H03, H04, H05, H06, H07, H08, H09;
    public bool K00, K01, K02, K03, K04, K05, K06, K07, K08, K09;
}

public class Node
{
    public int Value;
    public Node? Next;
}

public abstract class Shape
{
    public string? Name;
}

public class Circle : Shape
{
    public float Radius;
}

public class Rect : Shape
{
    public float Width, Height;
}

public class Polygon : Shape
{
    public List<Vec3>? Points;
}

public class Scene
{
    public List<Shape>? Shapes;
}

public class Bag
{
    public object?[] Items = Array.Empty<object?>();
}

public class GraphNode
{
    public int Id;
    public GraphNode? Parent;
    public List<GraphNode> Links = new();
}

public class Graph
{
    public List<GraphNode>? Nodes;
}

public class Inventory
{
    public Dictionary<string, int>? Counts;
    public Dictionary<int, RPerson>? Owners;
}

public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

[Flags]
public enum Tags { None = 0, A = 1, B = 2, C = 4, D = 8 }

public class Item
{
    public Rarity Rarity;
    public Tags Tags;
    public Rarity? Maybe;
    public int Count;
}

public class Conditional
{
    public bool HasExtra;
    public int Always;
    [SerializeIf("HasExtra")] public string? Extra;
    [SerializeIf("ShouldWrite")] public float Computed;
    public bool ShouldWrite() => Always % 2 == 0;
}

public class TextBlob
{
    public string? Ascii;
    public string? Unicode;
    public List<string>? Lines;
}

public class Small
{
    public int A;
    public float B;
    public string? C;
}
