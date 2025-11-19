using Aspect;

namespace Aspect.Tests;

/// <summary>
/// Tests for method interception with OnEntry, OnSuccess, OnException, OnExit lifecycle
/// </summary>
public class MethodInterceptionTests
{
    [Fact]
    public void OnEntry_ShouldBeCalledBeforeMethodExecution()
    {
        // Arrange
        var testClass = new TestClassWithMethodInterception();
        MethodLifecycleTracker.Reset();

        // Act
        testClass.SimpleMethod();

        // Assert
        Assert.Equal("OnEntry", MethodLifecycleTracker.Events[0]);
        Assert.Equal("Method", MethodLifecycleTracker.Events[1]);
    }

    [Fact]
    public void OnSuccess_ShouldBeCalledAfterSuccessfulMethodExecution()
    {
        // Arrange
        var testClass = new TestClassWithMethodInterception();
        MethodLifecycleTracker.Reset();

        // Act
        testClass.SimpleMethod();

        // Assert
        Assert.Contains("OnSuccess", MethodLifecycleTracker.Events);
        var successIndex = MethodLifecycleTracker.Events.IndexOf("OnSuccess");
        var methodIndex = MethodLifecycleTracker.Events.IndexOf("Method");
        Assert.True(successIndex > methodIndex);
    }

    [Fact]
    public void OnExit_ShouldBeCalledLastInSuccessPath()
    {
        // Arrange
        var testClass = new TestClassWithMethodInterception();
        MethodLifecycleTracker.Reset();

        // Act
        testClass.SimpleMethod();

        // Assert
        Assert.Equal(new[] { "OnEntry", "Method", "OnSuccess", "OnExit" }, MethodLifecycleTracker.Events);
    }

    [Fact]
    public void OnException_ShouldBeCalledWhenMethodThrows()
    {
        // Arrange
        var testClass = new TestClassWithMethodInterception();
        MethodLifecycleTracker.Reset();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => testClass.MethodThatThrows());

        Assert.Contains("OnException", MethodLifecycleTracker.Events);
        Assert.DoesNotContain("OnSuccess", MethodLifecycleTracker.Events);
    }

    [Fact]
    public void OnExit_ShouldBeCalledEvenWhenExceptionOccurs()
    {
        // Arrange
        var testClass = new TestClassWithMethodInterception();
        MethodLifecycleTracker.Reset();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => testClass.MethodThatThrows());

        // OnExit should still be called
        Assert.Equal("OnExit", MethodLifecycleTracker.Events.Last());
        Assert.Equal(new[] { "OnEntry", "Method", "OnException", "OnExit" }, MethodLifecycleTracker.Events);
    }

    [Fact]
    public void MethodExecutionArgs_ShouldProvideMethodDetails()
    {
        // Arrange
        var testClass = new TestClassWithMethodInterception();
        MethodContextCapture.Reset();

        // Act
        testClass.MethodWithParameters(42, "test", true);

        // Assert
        var context = MethodContextCapture.CapturedArgs;
        Assert.NotNull(context);
        Assert.Equal("MethodWithParameters", context.Method.Name);
        Assert.Equal(3, context.Arguments.Count);
        Assert.Equal(42, context.Arguments[0]);
        Assert.Equal("test", context.Arguments[1]);
        Assert.Equal(true, context.Arguments[2]);
        Assert.Equal(typeof(TestClassWithMethodInterception), context.Instance?.GetType());
    }

    [Fact]
    public void MethodExecutionArgs_ShouldAllowModifyingArguments()
    {
        // Arrange
        var testClass = new TestClassWithArgumentDoubler();
        ArgumentDoublerAspect.Reset();

        // Act
        var result = testClass.AddNumbers(5, 10);

        // Assert - arguments should have been doubled: (5*2) + (10*2) = 30
        Assert.Equal(30, result);
    }

    [Fact]
    public void MethodExecutionArgs_ShouldProvideReturnValue()
    {
        // Arrange
        var testClass = new TestClassWithReturnValueCapture();
        ReturnValueCaptureAspect.Reset();

        // Act
        var result = testClass.Calculate(10);

        // Assert
        Assert.Equal(20, result);
        Assert.Equal(20, ReturnValueCaptureAspect.CapturedReturnValue);
    }

    [Fact]
    public void MethodExecutionArgs_ShouldAllowModifyingReturnValue()
    {
        // Arrange
        var testClass = new TestClassWithReturnValueModifier();

        // Act
        var result = testClass.GetValue();

        // Assert - aspect should have modified the return value
        Assert.Equal(100, result); // Original was 42, aspect sets it to 100
    }

    [Fact]
    public void OnException_ShouldProvideExceptionDetails()
    {
        // Arrange
        var testClass = new TestClassWithExceptionCapture();
        ExceptionCaptureAspect.Reset();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => testClass.ThrowSpecificException());

        Assert.NotNull(ExceptionCaptureAspect.CapturedException);
        Assert.IsType<InvalidOperationException>(ExceptionCaptureAspect.CapturedException);
        Assert.Equal("Specific test exception", ExceptionCaptureAspect.CapturedException.Message);
    }

    [Fact]
    public void VoidMethod_ShouldStillTriggerAllLifecycleEvents()
    {
        // Arrange
        var testClass = new TestClassWithMethodInterception();
        MethodLifecycleTracker.Reset();

        // Act
        testClass.VoidMethod();

        // Assert
        Assert.Equal(new[] { "OnEntry", "VoidMethod", "OnSuccess", "OnExit" }, MethodLifecycleTracker.Events);
    }

    [Fact]
    public void GenericMethod_ShouldBeIntercepted()
    {
        // Arrange
        var testClass = new TestClassWithGenericMethod();
        MethodLifecycleTracker.Reset();

        // Act
        var result = testClass.GenericMethod<int>(42);

        // Assert
        Assert.Equal(42, result);
        Assert.Contains("OnEntry", MethodLifecycleTracker.Events);
        Assert.Contains("OnSuccess", MethodLifecycleTracker.Events);
    }
}

// Test infrastructure

public static class MethodLifecycleTracker
{
    public static List<string> Events = new();

    public static void Reset() => Events.Clear();
}

public static class MethodContextCapture
{
    public static MethodExecutionArgs? CapturedArgs;

    public static void Reset() => CapturedArgs = null;
}

public static class ReturnValueCaptureAspect
{
    public static object? CapturedReturnValue;

    public static void Reset() => CapturedReturnValue = null;
}

public static class ExceptionCaptureAspect
{
    public static Exception? CapturedException;

    public static void Reset() => CapturedException = null;
}

// Test classes

public class TestClassWithMethodInterception
{
    [MethodLifecycle]
    public void SimpleMethod()
    {
        MethodLifecycleTracker.Events.Add("Method");
    }

    [MethodLifecycle]
    public void MethodThatThrows()
    {
        MethodLifecycleTracker.Events.Add("Method");
        throw new InvalidOperationException("Test exception");
    }

    [MethodContextCapture]
    public void MethodWithParameters(int number, string text, bool flag)
    {
    }

    [MethodLifecycle]
    public void VoidMethod()
    {
        MethodLifecycleTracker.Events.Add("VoidMethod");
    }
}

public class TestClassWithArgumentDoubler
{
    [ArgumentDoubler]
    public int AddNumbers(int a, int b)
    {
        return a + b;
    }
}

public class TestClassWithReturnValueCapture
{
    [ReturnValueCapturer]
    public int Calculate(int input)
    {
        return input * 2;
    }
}

public class TestClassWithReturnValueModifier
{
    [ReturnValueModifier]
    public int GetValue()
    {
        return 42;
    }
}

public class TestClassWithExceptionCapture
{
    [ExceptionCapturer]
    public void ThrowSpecificException()
    {
        throw new InvalidOperationException("Specific test exception");
    }
}

public class TestClassWithGenericMethod
{
    [MethodLifecycle]
    public T GenericMethod<T>(T value)
    {
        return value;
    }
}
