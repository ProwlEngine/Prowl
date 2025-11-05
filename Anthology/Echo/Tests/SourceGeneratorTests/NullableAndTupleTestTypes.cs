using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

[GenerateSerializer]
public partial class ObjectWithNullables
{
    public int? NullableInt = null;
    public float? NullableFloat = null;
    public bool? NullableBool = null;
    public DateTime? NullableDateTime = null;
    public Guid? NullableGuid = null;
}

[GenerateSerializer]
public partial class ObjectWithNullablesSet
{
    public int? NullableInt = 42;
    public float? NullableFloat = 3.14f;
    public bool? NullableBool = true;
    public DateTime? NullableDateTime = new DateTime(2024, 1, 1);
    public Guid? NullableGuid = Guid.Empty;
}

[GenerateSerializer]
public partial class ObjectWithTuples
{
    public (int, string) Tuple2 = (1, "one");
    public (int, string, bool) Tuple3 = (2, "two", true);
    public (int, string, bool, float) Tuple4 = (3, "three", false, 3.14f);
}

[GenerateSerializer]
public partial class ObjectWithNamedTuples
{
    public (int Id, string Name) Person = (1, "John");
    public (float X, float Y, float Z) Position = (1.0f, 2.0f, 3.0f);
}

[GenerateSerializer]
public partial class ObjectWithNestedTuples
{
    public ((int, int), (string, string)) NestedTuple = ((1, 2), ("a", "b"));
    public List<(int Id, string Name)> TupleList = new();
}

[GenerateSerializer]
public partial class ObjectWithNullableTuples
{
    public (int?, string?) NullableTuple = (null, null);
    public (int?, string?)? CompletelyNullable = null;
}

[GenerateSerializer]
public partial struct NullableStructWithFields
{
    public int? NullableInt;
    public string? NullableString;
    public float? NullableFloat;
}
