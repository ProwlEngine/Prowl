using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

[GenerateSerializer]
public partial class ObjectWithSerializeField
{
    public int PublicField = 100;

    [SerializeField]
    private int privateSerializedField = 200;

    private int privateNotSerializedField = 300;

    [SerializeField]
    private string? privateSerializedString = "private";
}

[GenerateSerializer]
public partial class ObjectWithSerializeIgnore
{
    public int IncludedField = 100;

    [SerializeIgnore]
    public int IgnoredField = 200;

    public string IncludedString = "included";

    [SerializeIgnore]
    public string IgnoredString = "ignored";
}

[GenerateSerializer]
public partial class ObjectWithNonSerialized
{
    public int IncludedField = 100;

    [NonSerialized]
    public int IgnoredField = 200;

    public string IncludedString = "included";
}

[GenerateSerializer]
public partial class ObjectWithIgnoreOnNull
{
    [IgnoreOnNull]
    public string? NullString = null;

    [IgnoreOnNull]
    public int[]? NullArray = null;

    public string? AlwaysSerializedNull = null;

    [IgnoreOnNull]
    public string? NonNullString = "not null";
}

[GenerateSerializer]
public partial class ObjectWithSerializeIf
{
    public bool ShouldSerializeData = true;

    [SerializeIf(nameof(ShouldSerializeData))]
    public int ConditionalData = 42;

    [SerializeIf(nameof(ShouldSerializeData))]
    public string ConditionalString = "conditional";
}

[GenerateSerializer]
public partial class ObjectWithMultipleConditions
{
    public bool Condition1 = true;
    public bool Condition2 = false;

    [SerializeIf(nameof(Condition1))]
    public int Data1 = 1;

    [SerializeIf(nameof(Condition2))]
    public int Data2 = 2;
}

[GenerateSerializer]
public partial class ObjectWithFormerlySerializedAs
{
    [FormerlySerializedAs("OldName")]
    public int NewName = 100;

    [FormerlySerializedAs("old_value")]
    [FormerlySerializedAs("legacy_value")]
    public string MultipleOldNames = "current";
}

[GenerateSerializer]
public partial class ObjectWithCombinedAttributes
{
    public bool ShouldSerialize = true;

    [SerializeField]
    [SerializeIf(nameof(ShouldSerialize))]
    private int conditionalPrivateField = 100;

    [IgnoreOnNull]
    [FormerlySerializedAs("OldOptional")]
    public string? OptionalField = null;

    [SerializeIgnore]
    public int IgnoredEvenThoughPublic = 999;
}

[GenerateSerializer]
public partial class ObjectWithAllAttributes
{
    public int NormalField = 1;

    [SerializeField]
    private int privateField = 2;

    [SerializeIgnore]
    public int ignoredField = 3;

    [NonSerialized]
    public int alsoIgnored = 4;

    [IgnoreOnNull]
    public string? nullableField = null;

    public bool condition = true;

    [SerializeIf(nameof(condition))]
    public int conditionalField = 5;

    [FormerlySerializedAs("OldName")]
    public int renamedField = 6;

    [SerializeField]
    [IgnoreOnNull]
    [FormerlySerializedAs("oldPrivate")]
    private string? complexField = "complex";
}

[GenerateSerializer]
public partial class ObjectWithIgnoreOnNullAndSerializeIf
{
    public bool ShouldSerialize = false;

    [IgnoreOnNull]
    [SerializeIf(nameof(ShouldSerialize))]
    public string? ConditionalNullable = null;

    [IgnoreOnNull]
    [SerializeIf(nameof(ShouldSerialize))]
    public string? ConditionalNonNull = "value";
}
