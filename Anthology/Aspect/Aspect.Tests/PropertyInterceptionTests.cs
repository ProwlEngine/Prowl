using Aspect;

namespace Aspect.Tests;

/// <summary>
/// Tests for property interception with OnGetValue and OnSetValue
/// </summary>
public class PropertyInterceptionTests
{
    [Fact]
    public void OnGetValue_ShouldBeCalledWhenPropertyIsRead()
    {
        // Arrange
        var testClass = new TestClassWithPropertyInterception();
        PropertyAccessTracker.Reset();

        // Act
        var value = testClass.TrackedProperty;

        // Assert
        Assert.Contains("OnGetValue", PropertyAccessTracker.Events);
    }

    [Fact]
    public void OnSetValue_ShouldBeCalledWhenPropertyIsWritten()
    {
        // Arrange
        var testClass = new TestClassWithPropertyInterception();
        PropertyAccessTracker.Reset();

        // Act
        testClass.TrackedProperty = 42;

        // Assert
        Assert.Contains("OnSetValue", PropertyAccessTracker.Events);
    }

    [Fact]
    public void LocationInterceptionArgs_ShouldProvidePropertyValue()
    {
        // Arrange
        var testClass = new TestClassWithPropertyValueCapture { CapturedProperty = 123 };
        PropertyValueCapture.Reset();

        // Act
        var value = testClass.CapturedProperty;

        // Assert
        Assert.Equal(123, PropertyValueCapture.GetValue);
    }

    [Fact]
    public void LocationInterceptionArgs_ShouldProvideNewValueOnSet()
    {
        // Arrange
        var testClass = new TestClassWithPropertyValueCapture();
        PropertyValueCapture.Reset();

        // Act
        testClass.CapturedProperty = 456;

        // Assert
        Assert.Equal(456, PropertyValueCapture.SetValue);
    }

    [Fact]
    public void LocationInterceptionArgs_ShouldAllowModifyingGetValue()
    {
        // Arrange
        var testClass = new TestClassWithGetValueModifier { ModifiedProperty = 10 };

        // Act
        var value = testClass.ModifiedProperty;

        // Assert
        Assert.Equal(100, value); // Aspect multiplies by 10
    }

    [Fact]
    public void LocationInterceptionArgs_ShouldAllowModifyingSetValue()
    {
        // Arrange
        var testClass = new TestClassWithSetValueModifier();

        // Act
        testClass.ModifiedProperty = 5;

        // Assert - aspect should have doubled the value before setting
        Assert.Equal(10, testClass.ModifiedProperty);
    }

    [Fact]
    public void LocationInterceptionArgs_ShouldProvidePropertyInfo()
    {
        // Arrange
        var testClass = new TestClassWithPropertyInfoCapture();
        PropertyInfoCapture.Reset();

        // Act
        var value = testClass.NamedProperty;

        // Assert
        Assert.NotNull(PropertyInfoCapture.CapturedArgs);
        Assert.Equal("NamedProperty", PropertyInfoCapture.CapturedArgs.Property.Name);
        Assert.Equal(typeof(int), PropertyInfoCapture.CapturedArgs.Property.PropertyType);
    }

    [Fact]
    public void LocationInterceptionArgs_ShouldProvideInstance()
    {
        // Arrange
        var testClass = new TestClassWithPropertyInfoCapture();
        PropertyInfoCapture.Reset();

        // Act
        testClass.NamedProperty = 42;

        // Assert
        Assert.NotNull(PropertyInfoCapture.CapturedArgs);
        Assert.Same(testClass, PropertyInfoCapture.CapturedArgs.Instance);
    }

    [Fact]
    public void PropertyAspect_OnGetValue_ShouldSupportSkippingBackingField()
    {
        // Arrange
        var testClass = new TestClassWithComputedProperty();

        // Act
        var value = testClass.AlwaysReturnsHundred;

        // Assert
        Assert.Equal(100, value); // Aspect returns 100 without accessing backing field
    }

    [Fact]
    public void PropertyAspect_OnSetValue_ShouldSupportSkippingBackingField()
    {
        // Arrange
        var testClass = new TestClassWithValidatedProperty();
        ValidatedPropertyAspect.LastRejectedValue = null;

        // Act
        testClass.PositiveOnly = -5; // Should be rejected

        // Assert
        Assert.Equal(0, testClass.PositiveOnly); // Should remain at default value
        Assert.Equal(-5, ValidatedPropertyAspect.LastRejectedValue);
    }

    [Fact]
    public void PropertyAspect_ShouldWorkWithAutoProperties()
    {
        // Arrange
        var testClass = new TestClassWithAutoProperty();
        PropertyAccessTracker.Reset();

        // Act
        testClass.AutoProperty = 42;
        var value = testClass.AutoProperty;

        // Assert
        Assert.Contains("OnSetValue", PropertyAccessTracker.Events);
        Assert.Contains("OnGetValue", PropertyAccessTracker.Events);
        Assert.Equal(42, value);
    }

    [Fact]
    public void PropertyAspect_ShouldWorkWithReadOnlyProperties()
    {
        // Arrange
        var testClass = new TestClassWithReadOnlyProperty();
        PropertyAccessTracker.Reset();

        // Act
        var value = testClass.ReadOnlyProperty;

        // Assert
        Assert.Contains("OnGetValue", PropertyAccessTracker.Events);
        Assert.DoesNotContain("OnSetValue", PropertyAccessTracker.Events);
    }

    [Fact]
    public void PropertyAspect_ShouldWorkWithWriteOnlyProperties()
    {
        // Arrange
        var testClass = new TestClassWithWriteOnlyProperty();
        PropertyAccessTracker.Reset();

        // Act
        testClass.WriteOnlyProperty = 42;

        // Assert
        Assert.Contains("OnSetValue", PropertyAccessTracker.Events);
        Assert.DoesNotContain("OnGetValue", PropertyAccessTracker.Events);
    }

    [Fact]
    public void PropertyAspect_ShouldSupportDifferentPropertyTypes()
    {
        // Arrange
        var testClass = new TestClassWithVariousPropertyTypes();
        PropertyAccessTracker.Reset();

        // Act & Assert - String property
        testClass.StringProperty = "test";
        Assert.Equal("test", testClass.StringProperty);

        PropertyAccessTracker.Reset();

        // Act & Assert - Boolean property
        testClass.BoolProperty = true;
        Assert.True(testClass.BoolProperty);

        PropertyAccessTracker.Reset();

        // Act & Assert - Reference type property
        var obj = new object();
        testClass.ObjectProperty = obj;
        Assert.Same(obj, testClass.ObjectProperty);
    }

    [Fact]
    public void PropertyAspect_ShouldTrackOldAndNewValues()
    {
        // Arrange
        var testClass = new TestClassWithChangeTracking { TrackedValue = 10 };
        ChangeTrackingAspect.Reset();

        // Act
        testClass.TrackedValue = 20;

        // Assert
        Assert.Equal(10, ChangeTrackingAspect.OldValue);
        Assert.Equal(20, ChangeTrackingAspect.NewValue);
    }

    // Field interception tests

    [Fact]
    public void FieldInterception_OnGetValue_ShouldBeCalledWhenFieldIsRead()
    {
        // Arrange
        var testClass = new TestClassWithFieldInterception();
        testClass.TrackedField = 42; // Set initial value
        PropertyAccessTracker.Reset();

        // Act
        var value = testClass.TrackedField;

        // Assert
        Assert.Contains("OnGetValue", PropertyAccessTracker.Events);
    }

    [Fact]
    public void FieldInterception_OnSetValue_ShouldBeCalledWhenFieldIsWritten()
    {
        // Arrange
        var testClass = new TestClassWithFieldInterception();
        PropertyAccessTracker.Reset();

        // Act
        testClass.TrackedField = 42;

        // Assert
        Assert.Contains("OnSetValue", PropertyAccessTracker.Events);
    }

    [Fact]
    public void FieldInterception_ShouldProvideFieldValue()
    {
        // Arrange
        var testClass = new TestClassWithFieldValueCapture();
        testClass.CapturedField = 123;
        PropertyValueCapture.Reset();

        // Act
        var value = testClass.CapturedField;

        // Assert
        Assert.Equal(123, PropertyValueCapture.GetValue);
    }

    [Fact]
    public void FieldInterception_ShouldTrackAccessFromMethods()
    {
        // Arrange
        var testClass = new TestClassWithMultipleFields();
        PropertyAccessTracker.Reset();

        // Act - access fields through methods
        testClass.SetBothFields(10, "hello");
        var val = testClass.GetField1();

        // Assert - should have tracked the field accesses from within methods
        Assert.Contains("OnSetValue", PropertyAccessTracker.Events);
        Assert.Contains("OnGetValue", PropertyAccessTracker.Events);
    }
}

// Test infrastructure

public static class PropertyAccessTracker
{
    public static List<string> Events = new();

    public static void Reset() => Events.Clear();
}

public static class PropertyValueCapture
{
    public static object? GetValue;
    public static object? SetValue;

    public static void Reset()
    {
        GetValue = null;
        SetValue = null;
    }
}

public static class PropertyInfoCapture
{
    public static LocationInterceptionArgs? CapturedArgs;

    public static void Reset() => CapturedArgs = null;
}

public static class ValidatedPropertyAspect
{
    public static object? LastRejectedValue;
}

public static class ChangeTrackingAspect
{
    public static object? OldValue;
    public static object? NewValue;

    public static void Reset()
    {
        OldValue = null;
        NewValue = null;
    }
}

// Note: All aspect implementations are in TestAspects.cs

// Test classes

public class TestClassWithPropertyInterception
{
    [PropertyAccessTracking]
    public int TrackedProperty { get; set; }
}

public class TestClassWithPropertyValueCapture
{
    [PropertyValueCapture]
    public int CapturedProperty { get; set; }
}

public class TestClassWithGetValueModifier
{
    [GetValueModifier]
    public int ModifiedProperty { get; set; }
}

public class TestClassWithSetValueModifier
{
    [SetValueModifier]
    public int ModifiedProperty { get; set; }
}

public class TestClassWithPropertyInfoCapture
{
    [PropertyInfoCapture]
    public int NamedProperty { get; set; }
}

public class TestClassWithComputedProperty
{
    [ComputedProperty]
    public int AlwaysReturnsHundred { get; set; }
}

public class TestClassWithValidatedProperty
{
    [ValidationProperty]
    public int PositiveOnly { get; set; }
}

public class TestClassWithAutoProperty
{
    [PropertyAccessTracking]
    public int AutoProperty { get; set; }
}

public class TestClassWithReadOnlyProperty
{
    [PropertyAccessTracking]
    public int ReadOnlyProperty { get; } = 42;
}

public class TestClassWithWriteOnlyProperty
{
    private int _value;

    [PropertyAccessTracking]
    public int WriteOnlyProperty
    {
        set => _value = value;
    }
}

public class TestClassWithVariousPropertyTypes
{
    [PropertyAccessTracking]
    public string? StringProperty { get; set; }

    [PropertyAccessTracking]
    public bool BoolProperty { get; set; }

    [PropertyAccessTracking]
    public object? ObjectProperty { get; set; }
}

public class TestClassWithChangeTracking
{
    [ChangeTracker]
    public int TrackedValue { get; set; }
}

// Field interception test classes

public class TestClassWithFieldInterception
{
    [PropertyAccessTracking]
    public int TrackedField;
}

public class TestClassWithFieldValueCapture
{
    [PropertyValueCapture]
    public int CapturedField;
}

public class TestClassWithMultipleFields
{
    [PropertyAccessTracking]
    public int Field1;

    [PropertyAccessTracking]
    public string? Field2;

    public void SetBothFields(int val1, string? val2)
    {
        Field1 = val1;
        Field2 = val2;
    }

    public int GetField1() => Field1;
}
