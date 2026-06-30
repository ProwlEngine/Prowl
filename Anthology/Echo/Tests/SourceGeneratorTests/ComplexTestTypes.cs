using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

[GenerateSerializer]
public partial class SimpleClass
{
    public int Value = 0;
    public string Name = "";
}

[GenerateSerializer]
public partial class ObjectWithNestedGenerated
{
    public int Id = 0;
    public SimpleClass? Nested = null;
    public List<SimpleClass>? NestedList = null;
}

[GenerateSerializer]
public partial class DeeplyNestedObject
{
    public int Level = 0;
    public DeeplyNestedObject? Child = null;
}

[GenerateSerializer]
public partial struct GeneratedVector3
{
    public float X;
    public float Y;
    public float Z;
}

[GenerateSerializer]
public partial class ObjectWithGeneratedStruct
{
    public GeneratedVector3 Position;
    public GeneratedVector3 Velocity;
    public List<GeneratedVector3> Path = new();
}

[GenerateSerializer]
public partial class ObjectWithMixedTypes
{
    public int IntValue = 42;
    public string StringValue = "test";
    public float FloatValue = 3.14f;
    public bool BoolValue = true;
    public DateTime DateValue = DateTime.Now;
    public Guid GuidValue = Guid.NewGuid();
    public TimeSpan TimeValue = TimeSpan.FromHours(1);
}

[GenerateSerializer]
public partial class ObjectWithEnums
{
    public DayOfWeek Day = DayOfWeek.Monday;
    public FileMode Mode = FileMode.Open;
    public List<DayOfWeek> Days = new();
}

[GenerateSerializer]
public partial class ObjectWithComplexNesting
{
    public Dictionary<string, List<SimpleClass>> Data = new();
    public List<Dictionary<int, SimpleClass>> MoreData = new();
    public Dictionary<int, Dictionary<string, List<SimpleClass>>> EvenMoreData = new();
}

[GenerateSerializer]
public partial class ObjectWithCircularReference
{
    public string Name = "";
    public ObjectWithCircularReference? Next = null;
    public ObjectWithCircularReference? Previous = null;
}

[GenerateSerializer]
public partial class ObjectWithAllBuiltInTypes
{
    public byte ByteValue = 0;
    public sbyte SByteValue = 0;
    public short ShortValue = 0;
    public ushort UShortValue = 0;
    public int IntValue = 0;
    public uint UIntValue = 0;
    public long LongValue = 0;
    public ulong ULongValue = 0;
    public float FloatValue = 0;
    public double DoubleValue = 0;
    public decimal DecimalValue = 0;
    public char CharValue = ' ';
    public bool BoolValue = false;
    public string StringValue = "";
}

[GenerateSerializer]
public partial class ObjectWithNullableBuiltInTypes
{
    public byte? ByteValue = null;
    public sbyte? SByteValue = null;
    public short? ShortValue = null;
    public ushort? UShortValue = null;
    public int? IntValue = null;
    public uint? UIntValue = null;
    public long? LongValue = null;
    public ulong? ULongValue = null;
    public float? FloatValue = null;
    public double? DoubleValue = null;
    public decimal? DecimalValue = null;
    public char? CharValue = null;
    public bool? BoolValue = null;
}

[GenerateSerializer]
public partial class ObjectWithUriAndVersion
{
    public Uri? WebsiteUri = null;
    public Version? AppVersion = null;
}

[GenerateSerializer]
public partial class ObjectWithDateTimeTypes
{
    public DateTime DateTime = DateTime.Now;
    public DateTimeOffset DateTimeOffset = DateTimeOffset.Now;
    public TimeSpan TimeSpan = TimeSpan.Zero;
}

[GenerateSerializer]
public partial class ObjectWithComplexDictionaries
{
    public Dictionary<(int, string), SimpleClass> TupleKeyDict = new();
    public Dictionary<SimpleClass, List<int>> ObjectKeyDict = new();
    public Dictionary<Guid, Dictionary<string, List<SimpleClass>>> NestedDict = new();
}

[GenerateSerializer]
public partial class ObjectWithAnonymousTypeFields
{
    public object? AnonymousData = null;
}
