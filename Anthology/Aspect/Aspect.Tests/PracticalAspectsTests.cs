using Aspect;
using System.ComponentModel;
using System.Diagnostics;

namespace Aspect.Tests;

/// <summary>
/// Tests for practical, real-world aspect implementations
/// </summary>
public class PracticalAspectsTests
{
    #region Caching Tests

    [Fact]
    public void CacheAspect_ShouldCacheMethodResults()
    {
        // Arrange
        var calculator = new CachedCalculator();
        CacheAspect.ClearCache();

        // Act - first call should execute method
        var result1 = calculator.ExpensiveCalculation(5);
        var executionCount1 = calculator.ExecutionCount;

        // Act - second call with same args should use cache
        var result2 = calculator.ExpensiveCalculation(5);
        var executionCount2 = calculator.ExecutionCount;

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal(1, executionCount1);
        Assert.Equal(1, executionCount2); // Execution count should not increase
    }

    [Fact]
    public void CacheAspect_ShouldUseKeyBasedOnArguments()
    {
        // Arrange
        var calculator = new CachedCalculator();
        CacheAspect.ClearCache();

        // Act
        var result1 = calculator.ExpensiveCalculation(5);
        var result2 = calculator.ExpensiveCalculation(10); // Different argument
        var result3 = calculator.ExpensiveCalculation(5); // Same as first

        // Assert
        Assert.Equal(2, calculator.ExecutionCount); // Should execute twice (different args)
        Assert.NotEqual(result1, result2);
        Assert.Equal(result1, result3);
    }

    [Fact]
    public void CacheAspect_ShouldExpireAfterTimeout()
    {
        // This would test cache expiration if implemented
        // For now, just showing the API design
        var calculator = new CachedCalculatorWithExpiration();
        calculator.Calculate(5);
        // In real implementation, would wait and verify cache expired
    }

    #endregion

    #region Logging Tests

    [Fact]
    public void LoggingAspect_ShouldLogMethodEntryAndExit()
    {
        // Arrange
        var service = new LoggedService();
        LoggingAspect.Logs.Clear();

        // Act
        service.DoWork("test");

        // Assert
        Assert.Contains(LoggingAspect.Logs, log => log.Contains("Entering DoWork"));
        Assert.Contains(LoggingAspect.Logs, log => log.Contains("Exiting DoWork"));
    }

    [Fact]
    public void LoggingAspect_ShouldLogMethodArguments()
    {
        // Arrange
        var service = new LoggedService();
        LoggingAspect.Logs.Clear();

        // Act
        service.ProcessData(42, "test data");

        // Assert
        Assert.Contains(LoggingAspect.Logs, log => log.Contains("42"));
        Assert.Contains(LoggingAspect.Logs, log => log.Contains("test data"));
    }

    [Fact]
    public void LoggingAspect_ShouldLogExceptions()
    {
        // Arrange
        var service = new LoggedService();
        LoggingAspect.Logs.Clear();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.FailingMethod());

        Assert.Contains(LoggingAspect.Logs, log => log.Contains("Exception"));
        Assert.Contains(LoggingAspect.Logs, log => log.Contains("InvalidOperationException"));
    }

    [Fact]
    public void LoggingAspect_ShouldLogExecutionTime()
    {
        // Arrange
        var service = new LoggedService();
        LoggingAspect.Logs.Clear();

        // Act
        service.DoWork("test");

        // Assert
        Assert.Contains(LoggingAspect.Logs, log => log.Contains("ms") || log.Contains("milliseconds"));
    }

    #endregion

    #region NotifyPropertyChanged Tests

    [Fact]
    public void NotifyPropertyChangedAspect_ShouldRaisePropertyChanged()
    {
        // Arrange
        var model = new ObservableModel();
        var propertyChangedRaised = false;
        string? changedPropertyName = null;

        model.PropertyChanged += (sender, args) =>
        {
            propertyChangedRaised = true;
            changedPropertyName = args.PropertyName;
        };

        // Act
        model.Name = "New Name";

        // Assert
        Assert.True(propertyChangedRaised);
        Assert.Equal("Name", changedPropertyName);
    }

    [Fact]
    public void NotifyPropertyChangedAspect_ShouldNotRaiseIfValueUnchanged()
    {
        // Arrange
        var model = new ObservableModel { Name = "Initial" };
        var eventRaiseCount = 0;

        model.PropertyChanged += (sender, args) => eventRaiseCount++;

        // Act
        model.Name = "Initial"; // Same value

        // Assert
        Assert.Equal(0, eventRaiseCount);
    }

    [Fact]
    public void NotifyPropertyChangedAspect_ShouldProvideOldAndNewValues()
    {
        // Arrange
        var model = new ObservableModelWithChangeTracking { Age = 25 };

        // Act
        model.Age = 30;

        // Assert
        Assert.Equal(25, NotifyPropertyChangedAspect.LastOldValue);
        Assert.Equal(30, NotifyPropertyChangedAspect.LastNewValue);
    }

    #endregion

    #region Retry Tests

    [Fact]
    public void RetryAspect_ShouldRetryFailingMethod()
    {
        // Arrange
        var service = new UnreliableService();

        // Act
        service.UnreliableOperation();

        // Assert - should have retried and eventually succeeded
        Assert.True(service.AttemptCount > 1);
        Assert.True(service.AttemptCount <= 3); // Max retries
    }

    [Fact]
    public void RetryAspect_ShouldThrowAfterMaxRetries()
    {
        // Arrange
        var service = new AlwaysFailingService();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.AlwaysFailsOperation());
        Assert.Equal(3, service.AttemptCount); // Should have tried 3 times
    }

    [Fact]
    public void RetryAspect_ShouldNotRetryOnSuccess()
    {
        // Arrange
        var service = new ReliableService();

        // Act
        service.ReliableOperation();

        // Assert
        Assert.Equal(1, service.AttemptCount);
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public void TransactionAspect_ShouldCommitOnSuccess()
    {
        // Arrange
        var service = new TransactionalService();
        TransactionAspect.Reset();

        // Act
        service.SuccessfulTransaction();

        // Assert
        Assert.True(TransactionAspect.WasCommitted);
        Assert.False(TransactionAspect.WasRolledBack);
    }

    [Fact]
    public void TransactionAspect_ShouldRollbackOnException()
    {
        // Arrange
        var service = new TransactionalService();
        TransactionAspect.Reset();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.FailingTransaction());

        Assert.False(TransactionAspect.WasCommitted);
        Assert.True(TransactionAspect.WasRolledBack);
    }

    #endregion

    #region Authorization Tests

    [Fact]
    public void RequireAuthorizationAspect_ShouldAllowAuthorizedUser()
    {
        // Arrange
        AuthorizationAspect.CurrentUser = "admin";
        var service = new SecureService();

        // Act - should not throw
        service.AdminOnlyMethod();

        // Assert - if we got here, authorization passed
        Assert.True(true);
    }

    [Fact]
    public void RequireAuthorizationAspect_ShouldDenyUnauthorizedUser()
    {
        // Arrange
        AuthorizationAspect.CurrentUser = "guest";
        var service = new SecureService();

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => service.AdminOnlyMethod());
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ValidateArgumentsAspect_ShouldAllowValidInput()
    {
        // Arrange
        var service = new ValidatedService();

        // Act - should not throw
        service.ProcessPositiveNumber(42);

        // Assert
        Assert.True(true);
    }

    [Fact]
    public void ValidateArgumentsAspect_ShouldRejectInvalidInput()
    {
        // Arrange
        var service = new ValidatedService();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => service.ProcessPositiveNumber(-1));
    }

    [Fact]
    public void ValidateArgumentsAspect_ShouldCheckNullArguments()
    {
        // Arrange
        var service = new ValidatedService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.ProcessNonNullString(null!));
    }

    #endregion

    #region Performance Monitoring Tests

    [Fact]
    public void PerformanceMonitoringAspect_ShouldTrackExecutionTime()
    {
        // Arrange
        var service = new MonitoredService();
        PerformanceMonitor.Reset();

        // Act
        service.TrackedMethod();

        // Assert
        Assert.True(PerformanceMonitor.LastExecutionTime >= TimeSpan.Zero);
        Assert.Equal("TrackedMethod", PerformanceMonitor.LastMethodName);
    }

    [Fact]
    public void PerformanceMonitoringAspect_ShouldWarnOnSlowMethods()
    {
        // Arrange
        var service = new MonitoredService();
        PerformanceMonitor.Reset();

        // Act
        service.SlowMethod();

        // Assert
        Assert.True(PerformanceMonitor.PerformanceWarningRaised);
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public void ExceptionHandlingAspect_ShouldCatchAndLogExceptions()
    {
        // Arrange
        var service = new ResilientService();
        ExceptionHandlingAspect.Reset();

        // Act - should not throw
        service.MethodThatMayFail();

        // Assert
        Assert.NotNull(ExceptionHandlingAspect.LastException);
        Assert.IsType<InvalidOperationException>(ExceptionHandlingAspect.LastException);
    }

    [Fact]
    public void ExceptionHandlingAspect_ShouldReturnDefaultOnException()
    {
        // Arrange
        var service = new ResilientService();

        // Act
        var result = service.GetValueOrDefault();

        // Assert
        Assert.Equal(0, result); // Default value
    }

    #endregion
}

// Note: All aspect implementations are in TestAspects.cs

#region Test Classes

public class CachedCalculator
{
    public int ExecutionCount { get; private set; }

    [Cache]
    public int ExpensiveCalculation(int input)
    {
        ExecutionCount++;
        Thread.Sleep(10); // Simulate expensive operation
        return input * input;
    }
}

public class CachedCalculatorWithExpiration
{
    [CacheWithExpiration(ExpirationSeconds = 5)]
    public int Calculate(int input) => input * 2;
}

public class LoggedService
{
    [Logging]
    public void DoWork(string task)
    {
        // Do work
    }

    [Logging]
    public int ProcessData(int id, string data)
    {
        return id;
    }

    [Logging]
    public void FailingMethod()
    {
        throw new InvalidOperationException("Something went wrong");
    }
}

public class ObservableModel : INotifyPropertyChanged
{
    private string? _name;

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChanged]
    public string? Name
    {
        get => _name;
        set => _name = value;
    }
}

public class ObservableModelWithChangeTracking : INotifyPropertyChanged
{
    private int _age;

    public event PropertyChangedEventHandler? PropertyChanged;

    [NotifyPropertyChanged]
    public int Age
    {
        get => _age;
        set => _age = value;
    }
}

public class UnreliableService
{
    public int AttemptCount { get; private set; }
    private int _failuresBeforeSuccess = 2;

    [Retry(MaxRetries = 3)]
    public void UnreliableOperation()
    {
        AttemptCount++;
        if (AttemptCount < _failuresBeforeSuccess)
        {
            throw new InvalidOperationException("Temporary failure");
        }
    }
}

public class AlwaysFailingService
{
    public int AttemptCount { get; private set; }

    [Retry(MaxRetries = 3)]
    public void AlwaysFailsOperation()
    {
        AttemptCount++;
        throw new InvalidOperationException("Permanent failure");
    }
}

public class ReliableService
{
    public int AttemptCount { get; private set; }

    [Retry(MaxRetries = 3)]
    public void ReliableOperation()
    {
        AttemptCount++;
    }
}

public class TransactionalService
{
    [Transaction]
    public void SuccessfulTransaction()
    {
        // Perform database operations
    }

    [Transaction]
    public void FailingTransaction()
    {
        throw new InvalidOperationException("Transaction failed");
    }
}

public class SecureService
{
    [Authorization(RequiredRole = "admin")]
    public void AdminOnlyMethod()
    {
        // Secure operation
    }
}

public class ValidatedService
{
    [ValidatePositive(ParameterIndex = 0)]
    public void ProcessPositiveNumber(int number)
    {
    }

    [ValidateNotNull(ParameterIndex = 0)]
    public void ProcessNonNullString(string text)
    {
    }
}

public class MonitoredService
{
    [PerformanceMonitoring]
    public void TrackedMethod()
    {
        Thread.Sleep(10);
    }

    [PerformanceMonitoring(WarningThresholdMs = 50)]
    public void SlowMethod()
    {
        Thread.Sleep(100);
    }
}

public class ResilientService
{
    [ExceptionHandling]
    public void MethodThatMayFail()
    {
        throw new InvalidOperationException("This will be handled");
    }

    [ExceptionHandling]
    public int GetValueOrDefault()
    {
        throw new InvalidOperationException("This will return default");
    }
}

#endregion
