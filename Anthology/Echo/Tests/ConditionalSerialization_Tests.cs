// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Test;

#region Test Classes

public class ObjectWithPropertyCondition
{
    public bool ShouldSerialize { get; set; }

    [SerializeIf("ShouldSerialize")]
    public int ConditionalValue = 42;

    public string AlwaysSerializedValue = "always";
}

public class ObjectWithFieldCondition
{
    public bool shouldInclude = true;

    [SerializeIf("shouldInclude")]
    public string ConditionalData = "test data";

    public int NormalField = 100;
}

public class ObjectWithMethodCondition
{
    public int threshold = 10;

    [SerializeIf("ShouldSerializeValue")]
    public int Value = 50;

    public bool ShouldSerializeValue()
    {
        return Value > threshold;
    }
}

public class ObjectWithPrivatePropertyCondition
{
    private bool IsEnabled { get; set; } = true;

    [SerializeIf("IsEnabled")]
    public string Data = "private property test";

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}

public class ObjectWithPrivateFieldCondition
{
    private bool _isActive = false;

    [SerializeIf("_isActive")]
    public double Number = 3.14;

    public void Activate() => _isActive = true;
}

public class ObjectWithPrivateMethodCondition
{
    public string state = "active";

    [SerializeIf("CheckState")]
    public int[] Numbers = new[] { 1, 2, 3 };

    private bool CheckState()
    {
        return state == "active";
    }
}

public class ObjectWithMultipleConditionalFields
{
    public bool condition1 = true;
    public bool condition2 = false;

    [SerializeIf("condition1")]
    public string Field1 = "field1";

    [SerializeIf("condition2")]
    public string Field2 = "field2";

    public string AlwaysField = "always";
}

public class ObjectWithInvalidCondition
{
    [SerializeIf("NonExistentCondition")]
    public string Value = "should serialize anyway";
}

public class ObjectWithNonBoolCondition
{
    public int NotABool = 5;

    [SerializeIf("NotABool")]
    public string Value = "should serialize anyway";
}

public class ObjectWithNullConditionalField
{
    public bool ShouldSerialize = false;

    [SerializeIf("ShouldSerialize")]
    public string? ConditionalNull = null;

    public string? AlwaysNull = null;
}

public class ObjectWithConditionalAndIgnoreOnNull
{
    public bool ShouldSerialize = true;

    [SerializeIf("ShouldSerialize")]
    [IgnoreOnNull]
    public string? ConditionalValue = null;

    public int OtherValue = 42;
}

public class DerivedObjectWithCondition : ObjectWithPropertyCondition
{
    [SerializeIf("ShouldSerialize")]
    public float DerivedValue = 3.14f;
}

#endregion

public class ConditionalSerialization_Tests
{
    #region Property Condition Tests

    [Fact]
    public void TestPropertyCondition_WhenTrue_ShouldSerializeField()
    {
        var obj = new ObjectWithPropertyCondition
        {
            ShouldSerialize = true,
            ConditionalValue = 123
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPropertyCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(123, deserialized.ConditionalValue);
        Assert.Equal("always", deserialized.AlwaysSerializedValue);
    }

    [Fact]
    public void TestPropertyCondition_WhenFalse_ShouldNotSerializeField()
    {
        var obj = new ObjectWithPropertyCondition
        {
            ShouldSerialize = false,
            ConditionalValue = 123
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPropertyCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.ConditionalValue); // Default value
        Assert.Equal("always", deserialized.AlwaysSerializedValue);
    }

    #endregion

    #region Field Condition Tests

    [Fact]
    public void TestFieldCondition_WhenTrue_ShouldSerializeField()
    {
        var obj = new ObjectWithFieldCondition
        {
            shouldInclude = true,
            ConditionalData = "included data"
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithFieldCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("included data", deserialized.ConditionalData);
        Assert.Equal(100, deserialized.NormalField);
    }

    [Fact]
    public void TestFieldCondition_WhenFalse_ShouldNotSerializeField()
    {
        var obj = new ObjectWithFieldCondition
        {
            shouldInclude = false,
            ConditionalData = "excluded data"
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithFieldCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ConditionalData); // Default value for string
        Assert.Equal(100, deserialized.NormalField);
    }

    #endregion

    #region Method Condition Tests

    [Fact]
    public void TestMethodCondition_WhenTrue_ShouldSerializeField()
    {
        var obj = new ObjectWithMethodCondition
        {
            threshold = 10,
            Value = 50 // 50 > 10, so should serialize
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithMethodCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(50, deserialized.Value);
    }

    [Fact]
    public void TestMethodCondition_WhenFalse_ShouldNotSerializeField()
    {
        var obj = new ObjectWithMethodCondition
        {
            threshold = 100,
            Value = 50 // 50 < 100, so should not serialize
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithMethodCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.Value); // Default value
    }

    #endregion

    #region Private Member Tests

    [Fact]
    public void TestPrivatePropertyCondition_WhenTrue_ShouldSerializeField()
    {
        var obj = new ObjectWithPrivatePropertyCondition();
        obj.SetEnabled(true);

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPrivatePropertyCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("private property test", deserialized.Data);
    }

    [Fact]
    public void TestPrivatePropertyCondition_WhenFalse_ShouldNotSerializeField()
    {
        var obj = new ObjectWithPrivatePropertyCondition();
        obj.SetEnabled(false);

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPrivatePropertyCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Data); // Default value
    }

    [Fact]
    public void TestPrivateFieldCondition_WhenTrue_ShouldSerializeField()
    {
        var obj = new ObjectWithPrivateFieldCondition();
        obj.Activate();

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPrivateFieldCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(3.14, deserialized.Number);
    }

    [Fact]
    public void TestPrivateFieldCondition_WhenFalse_ShouldNotSerializeField()
    {
        var obj = new ObjectWithPrivateFieldCondition();
        // _isActive is false by default

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPrivateFieldCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(0.0, deserialized.Number); // Default value
    }

    [Fact]
    public void TestPrivateMethodCondition_WhenTrue_ShouldSerializeField()
    {
        var obj = new ObjectWithPrivateMethodCondition
        {
            state = "active"
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPrivateMethodCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Numbers);
        Assert.Equal(new[] { 1, 2, 3 }, deserialized.Numbers);
    }

    [Fact]
    public void TestPrivateMethodCondition_WhenFalse_ShouldNotSerializeField()
    {
        var obj = new ObjectWithPrivateMethodCondition
        {
            state = "inactive"
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithPrivateMethodCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Numbers); // Default value
    }

    #endregion

    #region Multiple Conditional Fields Tests

    [Fact]
    public void TestMultipleConditionalFields_MixedConditions()
    {
        var obj = new ObjectWithMultipleConditionalFields
        {
            condition1 = true,
            condition2 = false
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithMultipleConditionalFields>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("field1", deserialized.Field1); // Should be serialized
        Assert.Null(deserialized.Field2); // Should not be serialized
        Assert.Equal("always", deserialized.AlwaysField); // Always serialized
    }

    [Fact]
    public void TestMultipleConditionalFields_AllTrue()
    {
        var obj = new ObjectWithMultipleConditionalFields
        {
            condition1 = true,
            condition2 = true
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithMultipleConditionalFields>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("field1", deserialized.Field1);
        Assert.Equal("field2", deserialized.Field2);
        Assert.Equal("always", deserialized.AlwaysField);
    }

    [Fact]
    public void TestMultipleConditionalFields_AllFalse()
    {
        var obj = new ObjectWithMultipleConditionalFields
        {
            condition1 = false,
            condition2 = false
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithMultipleConditionalFields>(serialized);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Field1);
        Assert.Null(deserialized.Field2);
        Assert.Equal("always", deserialized.AlwaysField);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void TestInvalidCondition_ShouldDefaultToSerializing()
    {
        var obj = new ObjectWithInvalidCondition
        {
            Value = "test value"
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithInvalidCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("test value", deserialized.Value); // Should serialize when condition not found
    }

    [Fact]
    public void TestNonBoolCondition_ShouldDefaultToSerializing()
    {
        var obj = new ObjectWithNonBoolCondition
        {
            NotABool = 5,
            Value = "test value"
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithNonBoolCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal("test value", deserialized.Value); // Should serialize when condition is not bool
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void TestConditionalField_WithNull_WhenConditionTrue()
    {
        var obj = new ObjectWithNullConditionalField
        {
            ShouldSerialize = true,
            ConditionalNull = null
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithNullConditionalField>(serialized);

        Assert.NotNull(deserialized);
        // ConditionalNull should be serialized as null because condition is true
    }

    [Fact]
    public void TestConditionalField_WithNull_WhenConditionFalse()
    {
        var obj = new ObjectWithNullConditionalField
        {
            ShouldSerialize = false,
            ConditionalNull = null
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithNullConditionalField>(serialized);

        Assert.NotNull(deserialized);
        // ConditionalNull should not be serialized because condition is false
    }

    [Fact]
    public void TestConditionalWithIgnoreOnNull_WhenConditionTrueButValueNull()
    {
        var obj = new ObjectWithConditionalAndIgnoreOnNull
        {
            ShouldSerialize = true,
            ConditionalValue = null
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithConditionalAndIgnoreOnNull>(serialized);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ConditionalValue);
        Assert.Equal(42, deserialized.OtherValue);
    }

    [Fact]
    public void TestConditionalWithIgnoreOnNull_WhenConditionFalse()
    {
        var obj = new ObjectWithConditionalAndIgnoreOnNull
        {
            ShouldSerialize = false,
            ConditionalValue = null
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<ObjectWithConditionalAndIgnoreOnNull>(serialized);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ConditionalValue);
        Assert.Equal(42, deserialized.OtherValue);
    }

    #endregion

    #region Inheritance Tests

    [Fact]
    public void TestInheritance_ConditionFromBaseClass()
    {
        var obj = new DerivedObjectWithCondition
        {
            ShouldSerialize = true,
            ConditionalValue = 100,
            DerivedValue = 2.71f
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<DerivedObjectWithCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(100, deserialized.ConditionalValue);
        Assert.Equal(2.71f, deserialized.DerivedValue);
    }

    [Fact]
    public void TestInheritance_ConditionFromBaseClass_WhenFalse()
    {
        var obj = new DerivedObjectWithCondition
        {
            ShouldSerialize = false,
            ConditionalValue = 100,
            DerivedValue = 2.71f
        };

        var serialized = Serializer.Serialize(obj);
        var deserialized = Serializer.Deserialize<DerivedObjectWithCondition>(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.ConditionalValue);
        Assert.Equal(0f, deserialized.DerivedValue); // Should also not serialize
    }

    #endregion

    #region Binary Format Tests

    [Fact]
    public void TestConditionalSerialization_BinaryFormat()
    {
        var obj = new ObjectWithPropertyCondition
        {
            ShouldSerialize = false,
            ConditionalValue = 999
        };

        var echoObj = Serializer.Serialize(obj);
        var binary = echoObj.ToBytes();
        var deserialized = Serializer.Deserialize<ObjectWithPropertyCondition>(EchoObject.FromBytes(binary));

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.ConditionalValue); // Should not serialize
    }

    #endregion

    #region String Format Tests

    [Fact]
    public void TestConditionalSerialization_StringFormat()
    {
        var obj = new ObjectWithFieldCondition
        {
            shouldInclude = true,
            ConditionalData = "string format test"
        };

        var echoObj = Serializer.Serialize(obj);
        var stringFormat = echoObj.ToString();
        var deserialized = Serializer.Deserialize<ObjectWithFieldCondition>(EchoObject.FromString(stringFormat));

        Assert.NotNull(deserialized);
        Assert.Equal("string format test", deserialized.ConditionalData);
    }

    #endregion
}
