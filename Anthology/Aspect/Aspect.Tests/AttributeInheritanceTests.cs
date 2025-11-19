using Aspect;

namespace Aspect.Tests;

/// <summary>
/// Tests for attribute inheritance and class-level aspects
/// </summary>
public class AttributeInheritanceTests
{
    [Fact]
    public void ClassLevelAspect_ShouldApplyToAllMethods()
    {
        // Arrange
        var testClass = new TestClassWithClassLevelAspect();
        InheritanceTracker.Reset();

        // Act
        testClass.Method1();
        testClass.Method2();

        // Assert - both methods should have been intercepted
        Assert.Equal(2, InheritanceTracker.InterceptedMethods.Count);
        Assert.Contains("Method1", InheritanceTracker.InterceptedMethods);
        Assert.Contains("Method2", InheritanceTracker.InterceptedMethods);
    }

    [Fact]
    public void ClassLevelAspect_ShouldNotApplyToPropertiesByDefault()
    {
        // This test ensures methods and properties are handled separately
        // Properties should use LocationInterceptionAspect, not OnMethodBoundaryAspect

        // Arrange
        var testClass = new TestClassWithClassLevelAspect();
        InheritanceTracker.Reset();

        // Act
        testClass.SomeProperty = 42;
        var value = testClass.SomeProperty;

        // Assert - property access should not trigger method aspect
        Assert.Empty(InheritanceTracker.InterceptedMethods);
    }

    [Fact]
    public void InheritedAspect_ShouldApplyToDerivedClass_WhenMulticastInheritanceEnabled()
    {
        // Arrange
        var derivedClass = new DerivedClassFromAspectedBase();
        InheritanceTracker.Reset();

        // Act
        derivedClass.BaseMethod();

        // Assert
        Assert.Contains("BaseMethod", InheritanceTracker.InterceptedMethods);
    }

    [Fact(Skip = "Inheritance not supported")]
    public void InheritedAspect_ShouldApplyToOverriddenMethods()
    {
        // Arrange
        var derivedClass = new DerivedClassFromAspectedBase();
        InheritanceTracker.Reset();

        // Act
        derivedClass.VirtualMethod();

        // Assert
        Assert.Contains("VirtualMethod", InheritanceTracker.InterceptedMethods);
        Assert.Contains("Derived implementation", InheritanceTracker.Events);
    }

    [Fact(Skip = "Inheritance not supported")]
    public void InheritedAspect_ShouldNotApply_WhenMulticastInheritanceDisabled()
    {
        // Arrange
        var derivedClass = new DerivedClassFromNonInheritableAspect();
        InheritanceTracker.Reset();

        // Act
        derivedClass.BaseMethod();

        // Assert - aspect should not be applied to derived class
        Assert.Empty(InheritanceTracker.InterceptedMethods);
    }

    [Fact]
    public void MulticastAttributeUsage_TargetMembers_ShouldFilterByMemberName()
    {
        // Arrange
        var testClass = new TestClassWithTargetMemberFilter();
        InheritanceTracker.Reset();

        // Act
        testClass.IncludedMethod();
        testClass.ExcludedMethod();

        // Assert - only methods matching the filter should be intercepted
        Assert.Single(InheritanceTracker.InterceptedMethods);
        Assert.Contains("IncludedMethod", InheritanceTracker.InterceptedMethods);
    }

    [Fact]
    public void MulticastAttributeUsage_TargetMemberAttributes_ShouldFilterByAttributes()
    {
        // Arrange
        var testClass = new TestClassWithAttributeFilter();
        InheritanceTracker.Reset();

        // Act
        testClass.PublicMethod();
        testClass.PrivateMethodWrapper(); // Calls private method internally

        // Assert - only public methods should be intercepted
        Assert.Single(InheritanceTracker.InterceptedMethods);
        Assert.Contains("PublicMethod", InheritanceTracker.InterceptedMethods);
    }

    [Fact]
    public void MultipleClassLevelAspects_ShouldAllApply()
    {
        // Arrange
        var testClass = new TestClassWithMultipleClassAspects();
        InheritanceTracker.Reset();
        SecondaryTracker.Reset();

        // Act
        testClass.SomeMethod();

        // Assert - both aspects should have been applied
        Assert.Contains("SomeMethod", InheritanceTracker.InterceptedMethods);
        Assert.Contains("SomeMethod", SecondaryTracker.InterceptedMethods);
    }

    [Fact(Skip = "Inheritance not supported")]
    public void MethodLevelAspect_ShouldOverrideClassLevelAspect_WhenConfigured()
    {
        // Arrange
        var testClass = new TestClassWithMethodOverride();
        InheritanceTracker.Reset();
        OverrideTracker.Reset();

        // Act
        testClass.MethodWithSpecificAspect();

        // Assert - only the method-level aspect should apply
        Assert.Empty(InheritanceTracker.InterceptedMethods);
        Assert.Contains("MethodWithSpecificAspect", OverrideTracker.InterceptedMethods);
    }

    [Fact]
    public void AspectPriority_ShouldDetermineExecutionOrder()
    {
        // Arrange
        var testClass = new TestClassWithPrioritizedAspects();
        PriorityTracker.Reset();

        // Act
        testClass.PrioritizedMethod();

        // Assert - aspects should execute in priority order (highest first)
        Assert.Equal(new[] { "HighPriority", "Method", "LowPriority" }, PriorityTracker.ExecutionOrder);
    }

    [Fact(Skip = "Inheritance not supported")]
    public void InterfaceMethod_ShouldBeIntercepted_WhenAspectOnInterface()
    {
        // Arrange
        ITestInterface instance = new TestClassImplementingInterface();
        InheritanceTracker.Reset();

        // Act
        instance.InterfaceMethod();

        // Assert
        Assert.Contains("InterfaceMethod", InheritanceTracker.InterceptedMethods);
    }

    [Fact(Skip = "Inheritance not supported")]
    public void AbstractMethod_ShouldBeIntercepted_InDerivedClass()
    {
        // Arrange
        var testClass = new ConcreteClassFromAbstract();
        InheritanceTracker.Reset();

        // Act
        testClass.AbstractMethod();

        // Assert
        Assert.Contains("AbstractMethod", InheritanceTracker.InterceptedMethods);
    }
}

// Test infrastructure

public static class InheritanceTracker
{
    public static List<string> InterceptedMethods = new();
    public static List<string> Events = new();

    public static void Reset()
    {
        InterceptedMethods.Clear();
        Events.Clear();
    }
}

public static class SecondaryTracker
{
    public static List<string> InterceptedMethods = new();

    public static void Reset() => InterceptedMethods.Clear();
}

public static class OverrideTracker
{
    public static List<string> InterceptedMethods = new();

    public static void Reset() => InterceptedMethods.Clear();
}

public static class PriorityTracker
{
    public static List<string> ExecutionOrder = new();

    public static void Reset() => ExecutionOrder.Clear();
}

// Note: All aspect implementations are in TestAspects.cs

// Test classes

[Inheritable]
public class TestClassWithClassLevelAspect
{
    public void Method1() { }
    public void Method2() { }
    public int SomeProperty { get; set; }
}

[Inheritable]
public class BaseClassWithAspect
{
    public void BaseMethod() { }

    public virtual void VirtualMethod()
    {
        InheritanceTracker.Events.Add("Base implementation");
    }
}

public class DerivedClassFromAspectedBase : BaseClassWithAspect
{
    public override void VirtualMethod()
    {
        InheritanceTracker.Events.Add("Derived implementation");
    }
}

[NonInheritable]
public class BaseClassWithNonInheritableAspect
{
    public void BaseMethod() { }
}

public class DerivedClassFromNonInheritableAspect : BaseClassWithNonInheritableAspect
{
}

[TargetMemberFilter(TargetMembers = "Included*")]
public class TestClassWithTargetMemberFilter
{
    public void IncludedMethod() { }
    public void ExcludedMethod() { }
}

public class TestClassWithAttributeFilter
{
    [Inheritable]
    public void PublicMethod() { }

    private void PrivateMethod() { }

    public void PrivateMethodWrapper() => PrivateMethod();
}

[Inheritable]
[SecondaryAspect]
public class TestClassWithMultipleClassAspects
{
    public void SomeMethod() { }
}

[Inheritable]
public class TestClassWithMethodOverride
{
    [Override]
    public void MethodWithSpecificAspect() { }
}

public class TestClassWithPrioritizedAspects
{
    [HighPriority]
    [LowPriority]
    public void PrioritizedMethod()
    {
        PriorityTracker.ExecutionOrder.Add("Method");
    }
}

public interface ITestInterface
{
    [Inheritable]
    void InterfaceMethod();
}

public class TestClassImplementingInterface : ITestInterface
{
    public void InterfaceMethod() { }
}

[Inheritable]
public abstract class AbstractBaseClass
{
    public abstract void AbstractMethod();
}

public class ConcreteClassFromAbstract : AbstractBaseClass
{
    public override void AbstractMethod() { }
}
