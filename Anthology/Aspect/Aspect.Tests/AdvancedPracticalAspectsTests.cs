using Aspect;
using System.Collections.Concurrent;

namespace Aspect.Tests;

[Collection("Sequential")]
/// <summary>
/// Tests for advanced real-world aspect implementations
/// </summary>
public class AdvancedPracticalAspectsTests
{
    #region Rate Limiting Tests

    [Fact]
    public void RateLimiting_ShouldAllowRequestsWithinLimit()
    {
        // Arrange
        var service = new RateLimitedService();
        RateLimiter.Reset();

        // Act - make requests within the limit
        service.LimitedMethod();
        service.LimitedMethod();

        // Assert - should succeed
        Assert.Equal(2, service.CallCount);
    }

    [Fact]
    public void RateLimiting_ShouldThrowWhenLimitExceeded()
    {
        // Arrange
        var service = new RateLimitedService();
        RateLimiter.Reset();

        // Act - exhaust the limit
        service.LimitedMethod();
        service.LimitedMethod();
        service.LimitedMethod();

        // Assert - 4th call should throw
        Assert.Throws<InvalidOperationException>(() => service.LimitedMethod());
    }

    [Fact]
    public void RateLimiting_ShouldResetAfterTimeWindow()
    {
        // Arrange
        var service = new RateLimitedServiceWithReset();
        RateLimiterWithReset.Reset();

        // Act - make calls, wait, then make more
        service.TimedLimitedMethod();
        service.TimedLimitedMethod();

        // Simulate time passing (in real implementation would wait)
        RateLimiterWithReset.SimulateTimeElapsed();

        // Should allow new calls
        service.TimedLimitedMethod();

        // Assert
        Assert.Equal(3, service.CallCount);
    }

    #endregion

    #region Circuit Breaker Tests

    [Fact]
    public void CircuitBreaker_ShouldOpenAfterFailureThreshold()
    {
        // Arrange
        var service = new UnstableServiceWithCircuitBreaker();
        CircuitBreaker.Reset();

        // Act - cause multiple failures
        for (int i = 0; i < 3; i++)
        {
            try { service.UnstableOperation(); }
            catch { }
        }

        // Assert - circuit should be open
        Assert.True(CircuitBreaker.IsOpen);
        Assert.Throws<InvalidOperationException>(() => service.UnstableOperation());
    }

    [Fact]
    public void CircuitBreaker_ShouldAllowCallsWhenClosed()
    {
        // Arrange
        var service = new StableServiceWithCircuitBreaker();
        CircuitBreaker.Reset();

        // Act
        var result = service.StableOperation();

        // Assert - circuit should remain closed
        Assert.False(CircuitBreaker.IsOpen);
        Assert.Equal("Success", result);
    }

    [Fact]
    public void CircuitBreaker_ShouldHalfOpenAfterTimeout()
    {
        // Arrange
        var service = new RecoverableServiceWithCircuitBreaker();
        CircuitBreaker.Reset();

        // Trip the circuit
        for (int i = 0; i < 3; i++)
        {
            try { service.RecoverableOperation(false); }
            catch { }
        }

        // Simulate timeout
        CircuitBreaker.SimulateTimeout();

        // Act - should allow a test call
        service.RecoverableOperation(true);

        // Assert - successful call should close circuit
        Assert.False(CircuitBreaker.IsOpen);
    }

    #endregion

    #region Memoization Tests

    [Fact]
    public void Memoization_ShouldCacheBasedOnAllParameters()
    {
        // Arrange
        var calculator = new MemoizedCalculator();
        MemoizationCache.Clear();

        // Act
        var result1 = calculator.ComplexCalculation(5, 10);
        var result2 = calculator.ComplexCalculation(5, 10); // Same params
        var result3 = calculator.ComplexCalculation(5, 20); // Different params

        // Assert
        Assert.Equal(2, calculator.ExecutionCount); // Only executed twice
        Assert.Equal(result1, result2);
        Assert.NotEqual(result1, result3);
    }

    [Fact]
    public void Memoization_ShouldHandleNullParameters()
    {
        // Arrange
        var processor = new MemoizedStringProcessor();
        MemoizationCache.Clear();

        // Act
        var result1 = processor.Process(null);
        var result2 = processor.Process(null);
        var result3 = processor.Process("test");

        // Assert
        Assert.Equal(2, processor.ExecutionCount);
        Assert.Equal(result1, result2);
    }

    #endregion

    #region Dirty Tracking Tests

    [Fact]
    public void DirtyTracking_ShouldMarkPropertyAsDirtyOnChange()
    {
        // Arrange
        var model = new TrackedModel();
        DirtyTracker.Reset();

        // Act
        model.Name = "New Name";

        // Assert
        Assert.True(DirtyTracker.IsDirty("Name"));
        Assert.Contains("Name", DirtyTracker.DirtyProperties);
    }

    [Fact]
    public void DirtyTracking_ShouldNotMarkDirtyWhenValueUnchanged()
    {
        // Arrange
        var model = new TrackedModel { Age = 25 };
        DirtyTracker.Reset();

        // Act
        model.Age = 25; // Same value

        // Assert
        Assert.False(DirtyTracker.IsDirty("Age"));
    }

    [Fact]
    public void DirtyTracking_ShouldTrackMultipleProperties()
    {
        // Arrange
        var model = new TrackedModel();
        DirtyTracker.Reset();

        // Act
        model.Name = "John";
        model.Age = 30;
        model.Email = "john@example.com";

        // Assert
        Assert.Equal(3, DirtyTracker.DirtyProperties.Count);
        Assert.True(DirtyTracker.IsDirty("Name"));
        Assert.True(DirtyTracker.IsDirty("Age"));
        Assert.True(DirtyTracker.IsDirty("Email"));
    }

    [Fact]
    public void DirtyTracking_ShouldSupportReset()
    {
        // Arrange
        var model = new TrackedModel();
        DirtyTracker.Reset();
        model.Name = "Test";

        // Act
        DirtyTracker.ClearDirty();

        // Assert
        Assert.Empty(DirtyTracker.DirtyProperties);
    }

    #endregion

    #region Audit Logging Tests

    [Fact]
    public void AuditLogging_ShouldRecordMethodCall()
    {
        // Arrange
        var service = new AuditedService();
        AuditLog.Clear();
        AuditLog.CurrentUser = "user@example.com";

        // Act
        service.SensitiveOperation("data");

        // Assert
        Assert.Single(AuditLog.Entries);
        var entry = AuditLog.Entries.First();
        Assert.Equal("SensitiveOperation", entry.MethodName);
        Assert.Equal("user@example.com", entry.User);
        Assert.Contains("data", entry.Arguments);
    }

    [Fact]
    public void AuditLogging_ShouldRecordTimestamp()
    {
        // Arrange
        var service = new AuditedService();
        AuditLog.Clear();
        var beforeTime = DateTime.UtcNow;

        // Act
        service.SensitiveOperation("test");
        var afterTime = DateTime.UtcNow;

        // Assert
        var entry = AuditLog.Entries.First();
        Assert.True(entry.Timestamp >= beforeTime && entry.Timestamp <= afterTime);
    }

    [Fact]
    public void AuditLogging_ShouldRecordReturnValue()
    {
        // Arrange
        var service = new AuditedService();
        AuditLog.Clear();

        // Act
        var result = service.GetSensitiveData(123);

        // Assert
        var entry = AuditLog.Entries.First();
        Assert.Equal("Sensitive: 123", entry.ReturnValue);
    }

    #endregion

    #region Dependent Property Notification Tests

    [Fact]
    public void DependentPropertyNotification_ShouldNotifyDependentProperties()
    {
        // Arrange
        var person = new PersonWithDependentProperties();
        var notifications = new List<string>();
        person.PropertyChanged += (s, e) => notifications.Add(e.PropertyName!);

        // Act
        person.FirstName = "John";

        // Assert - should notify both FirstName and FullName
        Assert.Contains("FirstName", notifications);
        Assert.Contains("FullName", notifications);
    }

    [Fact]
    public void DependentPropertyNotification_ShouldHandleMultipleDependents()
    {
        // Arrange
        var rect = new RectangleWithDependentProperties();
        var notifications = new List<string>();
        rect.PropertyChanged += (s, e) => notifications.Add(e.PropertyName!);

        // Act
        rect.Width = 10;

        // Assert - Width change should notify Area
        Assert.Contains("Width", notifications);
        Assert.Contains("Area", notifications);
    }

    #endregion

    #region Complex Validation Tests

    [Fact]
    public void EmailValidation_ShouldAcceptValidEmail()
    {
        // Arrange
        var service = new ValidationService();

        // Act & Assert - should not throw
        service.RegisterUser("user@example.com", 25);
    }

    [Fact]
    public void EmailValidation_ShouldRejectInvalidEmail()
    {
        // Arrange
        var service = new ValidationService();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => service.RegisterUser("invalid-email", 25));
    }

    [Fact]
    public void RangeValidation_ShouldEnforceMinMax()
    {
        // Arrange
        var service = new ValidationService();

        // Act & Assert
        service.RegisterUser("user@example.com", 18); // Min
        service.RegisterUser("user@example.com", 100); // Max
        Assert.Throws<ArgumentException>(() => service.RegisterUser("user@example.com", 17)); // Too low
        Assert.Throws<ArgumentException>(() => service.RegisterUser("user@example.com", 101)); // Too high
    }

    [Fact]
    public void CompositeValidation_ShouldEnforceAllRules()
    {
        // Arrange
        var service = new ValidationService();

        // Act & Assert - both validations should apply
        Assert.Throws<ArgumentException>(() => service.RegisterUser("invalid", 25)); // Bad email
        Assert.Throws<ArgumentException>(() => service.RegisterUser("user@example.com", 10)); // Bad age
    }

    #endregion
}

#region Test Classes

// Rate Limiting
public class RateLimitedService
{
    public int CallCount { get; private set; }

    [RateLimit(MaxCalls = 3)]
    public void LimitedMethod()
    {
        CallCount++;
    }
}

public class RateLimitedServiceWithReset
{
    public int CallCount { get; private set; }

    [RateLimitWithTimeWindow(MaxCalls = 2, WindowSeconds = 60)]
    public void TimedLimitedMethod()
    {
        CallCount++;
    }
}

// Circuit Breaker
public class UnstableServiceWithCircuitBreaker
{
    [CircuitBreaker(FailureThreshold = 3)]
    public void UnstableOperation()
    {
        throw new InvalidOperationException("Service unavailable");
    }
}

public class StableServiceWithCircuitBreaker
{
    [CircuitBreaker(FailureThreshold = 3)]
    public string StableOperation()
    {
        return "Success";
    }
}

public class RecoverableServiceWithCircuitBreaker
{
    [CircuitBreaker(FailureThreshold = 3)]
    public void RecoverableOperation(bool succeed)
    {
        if (!succeed)
            throw new InvalidOperationException("Temporary failure");
    }
}

// Memoization
public class MemoizedCalculator
{
    public int ExecutionCount { get; private set; }

    [Memoize]
    public int ComplexCalculation(int x, int y)
    {
        ExecutionCount++;
        Thread.Sleep(10);
        return x * y + x + y;
    }
}

public class MemoizedStringProcessor
{
    public int ExecutionCount { get; private set; }

    [Memoize]
    public string Process(string? input)
    {
        ExecutionCount++;
        Thread.Sleep(10);
        return input?.ToUpper() ?? "NULL";
    }
}

// Dirty Tracking
public class TrackedModel
{
    [DirtyTracking]
    public string? Name { get; set; }

    [DirtyTracking]
    public int Age { get; set; }

    [DirtyTracking]
    public string? Email { get; set; }
}

// Audit Logging
public class AuditedService
{
    [AuditLog]
    public void SensitiveOperation(string data)
    {
        // Perform sensitive operation
    }

    [AuditLog]
    public string GetSensitiveData(int id)
    {
        return $"Sensitive: {id}";
    }
}

// Dependent Properties
public class PersonWithDependentProperties : System.ComponentModel.INotifyPropertyChanged
{
    private string? _firstName;
    private string? _lastName;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChanged]
    [AlsoNotify("FullName")]
    public string? FirstName
    {
        get => _firstName;
        set => _firstName = value;
    }

    [NotifyPropertyChanged]
    [AlsoNotify("FullName")]
    public string? LastName
    {
        get => _lastName;
        set => _lastName = value;
    }

    public string FullName => $"{FirstName} {LastName}";
}

public class RectangleWithDependentProperties : System.ComponentModel.INotifyPropertyChanged
{
    private double _width;
    private double _height;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChanged]
    [AlsoNotify("Area")]
    public double Width
    {
        get => _width;
        set => _width = value;
    }

    [NotifyPropertyChanged]
    [AlsoNotify("Area")]
    public double Height
    {
        get => _height;
        set => _height = value;
    }

    public double Area => Width * Height;
}

// Validation
public class ValidationService
{
    [ValidateParameters]
    public void RegisterUser(
        [ValidateEmail] string email,
        [ValidateRange(Min = 18, Max = 100)] int age)
    {
        // Register user
    }
}

#endregion
