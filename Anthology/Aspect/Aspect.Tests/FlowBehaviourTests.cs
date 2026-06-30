using Aspect;

namespace Aspect.Tests;

/// <summary>
/// Tests for FlowBehaviour to control execution flow (Continue, Return, ThrowException)
/// </summary>
public class FlowBehaviourTests
{
    [Fact]
    public void FlowBehaviour_Continue_ShouldExecuteMethod()
    {
        // Arrange
        var testClass = new TestClassWithContinueBehaviour();
        FlowBehaviourTracker.Reset();

        // Act
        var result = testClass.NormalMethod();

        // Assert
        Assert.Equal(42, result);
        Assert.Contains("Method executed", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void FlowBehaviour_Return_ShouldSkipMethodExecution()
    {
        // Arrange
        var testClass = new TestClassWithReturnBehaviour();
        FlowBehaviourTracker.Reset();

        // Act
        var result = testClass.MethodThatShouldBeSkipped();

        // Assert
        Assert.Equal(100, result); // Return value set by aspect
        Assert.DoesNotContain("Method executed", FlowBehaviourTracker.Events);
        Assert.Contains("OnEntry", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void FlowBehaviour_Return_ShouldNotTriggerOnSuccess()
    {
        // Arrange
        var testClass = new TestClassWithReturnBehaviour();
        FlowBehaviourTracker.Reset();

        // Act
        testClass.MethodThatShouldBeSkipped();

        // Assert
        Assert.DoesNotContain("OnSuccess", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void FlowBehaviour_Return_ShouldStillTriggerOnExit()
    {
        // Arrange
        var testClass = new TestClassWithReturnBehaviour();
        FlowBehaviourTracker.Reset();

        // Act
        testClass.MethodThatShouldBeSkipped();

        // Assert
        Assert.Contains("OnExit", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void FlowBehaviour_Return_ShouldNotTriggerOnException()
    {
        // Arrange
        var testClass = new TestClassWithReturnBehaviour();
        FlowBehaviourTracker.Reset();

        // Act
        testClass.MethodThatShouldBeSkipped();

        // Assert - No exception should occur
        Assert.DoesNotContain("OnException", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void FlowBehaviour_ThrowException_ShouldThrowCustomException()
    {
        // Arrange
        var testClass = new TestClassWithThrowBehaviour();

        // Act & Assert
        var exception = Assert.Throws<UnauthorizedAccessException>(
            () => testClass.MethodThatShouldThrow()
        );

        Assert.Equal("Access denied by aspect", exception.Message);
    }

    [Fact]
    public void FlowBehaviour_ThrowException_ShouldNotExecuteMethod()
    {
        // Arrange
        var testClass = new TestClassWithThrowBehaviour();
        FlowBehaviourTracker.Reset();

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => testClass.MethodThatShouldThrow());

        Assert.DoesNotContain("Method executed", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void FlowBehaviour_ThrowException_ShouldTriggerOnException()
    {
        // Arrange
        var testClass = new TestClassWithThrowBehaviour();
        FlowBehaviourTracker.Reset();

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => testClass.MethodThatShouldThrow());

        Assert.Contains("OnException", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void FlowBehaviour_Return_WithVoidMethod_ShouldWork()
    {
        // Arrange
        var testClass = new TestClassWithReturnOnVoid();
        FlowBehaviourTracker.Reset();

        // Act
        testClass.VoidMethodThatShouldBeSkipped();

        // Assert
        Assert.DoesNotContain("Method executed", FlowBehaviourTracker.Events);
        Assert.Contains("OnEntry", FlowBehaviourTracker.Events);
        Assert.Contains("OnExit", FlowBehaviourTracker.Events);
    }

    [Fact]
    public void OnException_FlowBehaviour_Continue_ShouldRethrowException()
    {
        // Arrange
        var testClass = new TestClassWithExceptionContinue();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => testClass.MethodThatThrows());
    }

    [Fact]
    public void OnException_FlowBehaviour_Return_ShouldSuppressException()
    {
        // Arrange
        var testClass = new TestClassWithExceptionReturn();

        // Act - should not throw
        var result = testClass.MethodThatThrowsButIsSuppressed();

        // Assert
        Assert.Equal(999, result); // Return value set by aspect in OnException
    }

    [Fact]
    public void OnException_FlowBehaviour_ThrowException_ShouldReplaceException()
    {
        // Arrange
        var testClass = new TestClassWithExceptionReplacement();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => testClass.MethodThatThrowsInvalidOperation()
        );

        Assert.Equal("Replaced exception", exception.Message);
    }

    [Fact]
    public void FlowBehaviour_ConditionalReturn_ShouldWorkBasedOnCondition()
    {
        // Arrange
        var testClass = new TestClassWithConditionalReturn();
        FlowBehaviourTracker.Reset();

        // Act - with value that should skip
        var result1 = testClass.ConditionalMethod(0);

        FlowBehaviourTracker.Reset();

        // Act - with value that should continue
        var result2 = testClass.ConditionalMethod(5);

        // Assert
        Assert.Equal(-1, result1); // Returned by aspect
        Assert.Equal(10, result2); // Returned by method (5 * 2)
    }

    [Fact]
    public void FlowBehaviour_MethodLevelAndParameterValidation_ShouldWork()
    {
        // Arrange
        var testClass = new TestClassWithValidation();

        // Act & Assert - valid input
        var result1 = testClass.ValidatedMethod(10);
        Assert.Equal(10, result1);

        // Act & Assert - invalid input (should be rejected by aspect)
        Assert.Throws<ArgumentException>(() => testClass.ValidatedMethod(-5));
    }
}

// Test infrastructure

public static class FlowBehaviourTracker
{
    public static List<string> Events = new();

    public static void Reset() => Events.Clear();
}

// Note: All aspect implementations are in TestAspects.cs

// Test classes

public class TestClassWithContinueBehaviour
{
    [ContinueBehaviour]
    public int NormalMethod()
    {
        FlowBehaviourTracker.Events.Add("Method executed");
        return 42;
    }
}

public class TestClassWithReturnBehaviour
{
    [ReturnBehaviour]
    public int MethodThatShouldBeSkipped()
    {
        FlowBehaviourTracker.Events.Add("Method executed");
        return 42;
    }
}

public class TestClassWithThrowBehaviour
{
    [ThrowBehaviour]
    public int MethodThatShouldThrow()
    {
        FlowBehaviourTracker.Events.Add("Method executed");
        return 42;
    }
}

public class TestClassWithReturnOnVoid
{
    [VoidReturnBehaviour]
    public void VoidMethodThatShouldBeSkipped()
    {
        FlowBehaviourTracker.Events.Add("Method executed");
    }
}

public class TestClassWithExceptionContinue
{
    [ExceptionContinue]
    public void MethodThatThrows()
    {
        throw new InvalidOperationException("Original exception");
    }
}

public class TestClassWithExceptionReturn
{
    [ExceptionReturn]
    public int MethodThatThrowsButIsSuppressed()
    {
        throw new InvalidOperationException("This should be suppressed");
    }
}

public class TestClassWithExceptionReplacement
{
    [ExceptionReplacement]
    public void MethodThatThrowsInvalidOperation()
    {
        throw new InvalidOperationException("Original exception");
    }
}

public class TestClassWithConditionalReturn
{
    [ConditionalReturn]
    public int ConditionalMethod(int value)
    {
        FlowBehaviourTracker.Events.Add("Method executed");
        return value * 2;
    }
}

public class TestClassWithValidation
{
    [Validation]
    public int ValidatedMethod(int value)
    {
        return value;
    }
}
