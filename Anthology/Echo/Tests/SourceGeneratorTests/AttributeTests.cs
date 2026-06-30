using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

public class AttributeTests
{
    private T RoundTrip<T>(T value)
    {
        var serialized = Serializer.Serialize(value);
        return Serializer.Deserialize<T>(serialized);
    }

    [Fact]
    public void ObjectWithSerializeField_SerializesPrivateFields()
    {
        var original = new ObjectWithSerializeField();
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Public field should be serialized
        Assert.True(tag.Contains("PublicField"));
        Assert.Equal(100, result.PublicField);

        // Private field with [SerializeField] should be serialized
        Assert.True(tag.Contains("privateSerializedField"));

        // Private field with [SerializeField] should be serialized
        Assert.True(tag.Contains("privateSerializedString"));
    }

    [Fact]
    public void ObjectWithSerializeIgnore_IgnoresMarkedFields()
    {
        var original = new ObjectWithSerializeIgnore
        {
            IncludedField = 999,
            IgnoredField = 888,
            IncludedString = "test",
            IgnoredString = "should not serialize"
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Included fields should be serialized
        Assert.True(tag.Contains("IncludedField"));
        Assert.True(tag.Contains("IncludedString"));
        Assert.Equal(999, result.IncludedField);
        Assert.Equal("test", result.IncludedString);

        // Ignored fields should NOT be serialized
        Assert.False(tag.Contains("IgnoredField"));
        Assert.False(tag.Contains("IgnoredString"));
        // Ignored fields should have default values after deserialization
        Assert.Equal(200, result.IgnoredField); // default from type
        Assert.Equal("ignored", result.IgnoredString); // default from type
    }

    [Fact]
    public void ObjectWithNonSerialized_IgnoresMarkedFields()
    {
        var original = new ObjectWithNonSerialized
        {
            IncludedField = 555,
            IgnoredField = 444
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.True(tag.Contains("IncludedField"));
        Assert.False(tag.Contains("IgnoredField"));
        Assert.Equal(555, result.IncludedField);
    }

    [Fact]
    public void ObjectWithIgnoreOnNull_SkipsNullFields()
    {
        var original = new ObjectWithIgnoreOnNull
        {
            NullString = null,
            NullArray = null,
            AlwaysSerializedNull = null,
            NonNullString = "not null"
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Fields with [IgnoreOnNull] and null value should NOT be serialized
        Assert.False(tag.Contains("NullString"));
        Assert.False(tag.Contains("NullArray"));

        // Field without [IgnoreOnNull] should be serialized even if null
        Assert.True(tag.Contains("AlwaysSerializedNull"));

        // Field with [IgnoreOnNull] but non-null value should be serialized
        Assert.True(tag.Contains("NonNullString"));
        Assert.Equal("not null", result.NonNullString);
    }

    [Fact]
    public void ObjectWithIgnoreOnNull_SerializesNonNullFields()
    {
        var original = new ObjectWithIgnoreOnNull
        {
            NullString = "now has value",
            NullArray = new[] { 1, 2, 3 },
            AlwaysSerializedNull = "also has value",
            NonNullString = "still not null"
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // All fields should be serialized since they're not null
        Assert.True(tag.Contains("NullString"));
        Assert.True(tag.Contains("NullArray"));
        Assert.True(tag.Contains("AlwaysSerializedNull"));
        Assert.True(tag.Contains("NonNullString"));

        Assert.Equal("now has value", result.NullString);
        Assert.Equal(new[] { 1, 2, 3 }, result.NullArray);
        Assert.Equal("also has value", result.AlwaysSerializedNull);
        Assert.Equal("still not null", result.NonNullString);
    }

    [Fact]
    public void ObjectWithSerializeIf_OnlySerializesWhenConditionTrue()
    {
        var original = new ObjectWithSerializeIf
        {
            ShouldSerializeData = true,
            ConditionalData = 999,
            ConditionalString = "conditional value"
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Condition field should always be serialized
        Assert.True(tag.Contains("ShouldSerializeData"));

        // Conditional fields should be serialized when condition is true
        Assert.True(tag.Contains("ConditionalData"));
        Assert.True(tag.Contains("ConditionalString"));
        Assert.Equal(999, result.ConditionalData);
        Assert.Equal("conditional value", result.ConditionalString);
    }

    [Fact]
    public void ObjectWithSerializeIf_SkipsFieldsWhenConditionFalse()
    {
        var original = new ObjectWithSerializeIf
        {
            ShouldSerializeData = false,
            ConditionalData = 999,
            ConditionalString = "should not serialize"
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Condition field should be serialized
        Assert.True(tag.Contains("ShouldSerializeData"));

        // Conditional fields should NOT be serialized when condition is false
        Assert.False(tag.Contains("ConditionalData"));
        Assert.False(tag.Contains("ConditionalString"));
        // Should have default values
        Assert.Equal(42, result.ConditionalData); // default from type
        Assert.Equal("conditional", result.ConditionalString); // default from type
    }

    [Fact]
    public void ObjectWithMultipleConditions_RespectsEachCondition()
    {
        var original = new ObjectWithMultipleConditions
        {
            Condition1 = true,
            Condition2 = false,
            Data1 = 100,
            Data2 = 200
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Data1 should be serialized (Condition1 is true)
        Assert.True(tag.Contains("Data1"));
        Assert.Equal(100, result.Data1);

        // Data2 should NOT be serialized (Condition2 is false)
        Assert.False(tag.Contains("Data2"));
        Assert.Equal(2, result.Data2); // default value
    }

    [Fact]
    public void ObjectWithFormerlySerializedAs_DeserializesOldName()
    {
        // Create an EchoObject with the old field name
        var tag = EchoObject.NewCompound();
        tag.Add("OldName", new EchoObject(EchoType.Int, 777));

        var result = Serializer.Deserialize<ObjectWithFormerlySerializedAs>(tag);

        Assert.NotNull(result);
        Assert.Equal(777, result.NewName);
    }

    [Fact]
    public void ObjectWithFormerlySerializedAs_PrefersNewName()
    {
        // Create an EchoObject with both old and new field names
        var tag = EchoObject.NewCompound();
        tag.Add("NewName", new EchoObject(EchoType.Int, 888));
        tag.Add("OldName", new EchoObject(EchoType.Int, 999));

        var result = Serializer.Deserialize<ObjectWithFormerlySerializedAs>(tag);

        Assert.NotNull(result);
        // Should prefer the new name
        Assert.Equal(888, result.NewName);
    }

    [Fact]
    public void ObjectWithFormerlySerializedAs_HandlesMultipleOldNames()
    {
        // Test with first old name
        var tag1 = EchoObject.NewCompound();
        tag1.Add("old_value", new EchoObject(EchoType.String, "from old_value"));
        var result1 = Serializer.Deserialize<ObjectWithFormerlySerializedAs>(tag1);
        Assert.Equal("from old_value", result1.MultipleOldNames);

        // Test with second old name
        var tag2 = EchoObject.NewCompound();
        tag2.Add("legacy_value", new EchoObject(EchoType.String, "from legacy_value"));
        var result2 = Serializer.Deserialize<ObjectWithFormerlySerializedAs>(tag2);
        Assert.Equal("from legacy_value", result2.MultipleOldNames);

        // Test with new name (should prefer this)
        var tag3 = EchoObject.NewCompound();
        tag3.Add("MultipleOldNames", new EchoObject(EchoType.String, "from new name"));
        tag3.Add("old_value", new EchoObject(EchoType.String, "from old"));
        var result3 = Serializer.Deserialize<ObjectWithFormerlySerializedAs>(tag3);
        Assert.Equal("from new name", result3.MultipleOldNames);
    }

    [Fact]
    public void ObjectWithCombinedAttributes_HandlesAllCombinations()
    {
        var original = new ObjectWithCombinedAttributes
        {
            ShouldSerialize = true,
            OptionalField = "has value",
            IgnoredEvenThoughPublic = 777
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Private field with SerializeIf (condition is true) should be serialized
        Assert.True(tag.Contains("conditionalPrivateField"));

        // Field with IgnoreOnNull (has value) should be serialized
        Assert.True(tag.Contains("OptionalField"));
        Assert.Equal("has value", result.OptionalField);

        // Field with SerializeIgnore should NOT be serialized
        Assert.False(tag.Contains("IgnoredEvenThoughPublic"));
    }

    [Fact]
    public void ObjectWithAllAttributes_ComplexScenario()
    {
        var original = new ObjectWithAllAttributes
        {
            NormalField = 10,
            nullableField = "not null",
            condition = true,
            conditionalField = 50,
            renamedField = 60
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.True(tag.Contains("NormalField"));
        Assert.True(tag.Contains("privateField"));
        Assert.False(tag.Contains("ignoredField"));
        Assert.False(tag.Contains("alsoIgnored"));
        Assert.True(tag.Contains("nullableField"));
        Assert.True(tag.Contains("conditionalField"));
        Assert.True(tag.Contains("renamedField"));
        Assert.True(tag.Contains("complexField"));

        Assert.Equal(10, result.NormalField);
        Assert.Equal("not null", result.nullableField);
        Assert.Equal(50, result.conditionalField);
        Assert.Equal(60, result.renamedField);
    }

    [Fact]
    public void ObjectWithIgnoreOnNullAndSerializeIf_BothConditionsFalse()
    {
        var original = new ObjectWithIgnoreOnNullAndSerializeIf
        {
            ShouldSerialize = false,
            ConditionalNullable = null,
            ConditionalNonNull = "value"
        };
        var tag = Serializer.Serialize(original);

        // Both fields should NOT be serialized:
        // - ShouldSerialize is false
        Assert.False(tag.Contains("ConditionalNullable"));
        Assert.False(tag.Contains("ConditionalNonNull"));
    }

    [Fact]
    public void ObjectWithIgnoreOnNullAndSerializeIf_ConditionTrueButNull()
    {
        var original = new ObjectWithIgnoreOnNullAndSerializeIf
        {
            ShouldSerialize = true,
            ConditionalNullable = null,
            ConditionalNonNull = "value"
        };
        var tag = Serializer.Serialize(original);

        // ConditionalNullable should NOT be serialized (IgnoreOnNull and value is null)
        Assert.False(tag.Contains("ConditionalNullable"));

        // ConditionalNonNull should be serialized (condition is true and not null)
        Assert.True(tag.Contains("ConditionalNonNull"));
    }

    [Fact]
    public void ObjectWithIgnoreOnNullAndSerializeIf_BothConditionsTrue()
    {
        var original = new ObjectWithIgnoreOnNullAndSerializeIf
        {
            ShouldSerialize = true,
            ConditionalNullable = "has value",
            ConditionalNonNull = "also has value"
        };
        var tag = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.NotNull(result);
        // Both should be serialized
        Assert.True(tag.Contains("ConditionalNullable"));
        Assert.True(tag.Contains("ConditionalNonNull"));
        Assert.Equal("has value", result.ConditionalNullable);
        Assert.Equal("also has value", result.ConditionalNonNull);
    }
}
