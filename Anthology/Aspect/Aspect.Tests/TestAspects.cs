using Aspect;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Aspect.Tests;

/// <summary>
/// Shared test aspect implementations used across multiple test files.
/// These aspects are for testing purposes only and demonstrate the API.
/// </summary>

// Simple tracking aspects
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class MethodLifecycleAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        MethodLifecycleTracker.Events.Add("OnEntry");
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        MethodLifecycleTracker.Events.Add("OnSuccess");
    }

    public override void OnException(MethodExecutionArgs args)
    {
        MethodLifecycleTracker.Events.Add("OnException");
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        MethodLifecycleTracker.Events.Add("OnExit");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class MethodContextCaptureAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        MethodContextCapture.CapturedArgs = args;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ArgumentDoublerAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        for (int i = 0; i < args.Arguments.Count; i++)
        {
            if (args.Arguments[i] is int intValue)
            {
                args.Arguments[i] = intValue * 2;
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ReturnValueModifierAttribute : OnMethodBoundaryAspect
{
    public override void OnSuccess(MethodExecutionArgs args)
    {
        args.ReturnValue = 100;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ReturnValueCapturerAttribute : OnMethodBoundaryAspect
{
    public override void OnSuccess(MethodExecutionArgs args)
    {
        ReturnValueCaptureAspect.CapturedReturnValue = args.ReturnValue;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ExceptionCapturerAttribute : OnMethodBoundaryAspect
{
    public override void OnException(MethodExecutionArgs args)
    {
        ExceptionCaptureAspect.CapturedException = args.Exception;
    }
}

// Flow behavior aspects
[AttributeUsage(AttributeTargets.Method)]
public class ContinueBehaviourAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnEntry");
        args.FlowBehavior = FlowBehavior.Continue;
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnSuccess");
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnExit");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ReturnBehaviourAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnEntry");
        args.ReturnValue = 100;
        args.FlowBehavior = FlowBehavior.Return;
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnSuccess");
    }

    public override void OnException(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnException");
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnExit");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ThrowBehaviourAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnEntry");
        args.FlowBehavior = FlowBehavior.ThrowException;
        args.Exception = new UnauthorizedAccessException("Access denied by aspect");
    }

    public override void OnException(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnException");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class VoidReturnBehaviourAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnEntry");
        args.FlowBehavior = FlowBehavior.Return;
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnExit");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ExceptionContinueAttribute : OnMethodBoundaryAspect
{
    public override void OnException(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnException");
        args.FlowBehavior = FlowBehavior.Continue;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ExceptionReturnAttribute : OnMethodBoundaryAspect
{
    public override void OnException(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnException");
        args.ReturnValue = 999;
        args.FlowBehavior = FlowBehavior.Return;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ExceptionReplacementAttribute : OnMethodBoundaryAspect
{
    public override void OnException(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnException");
        args.Exception = new ArgumentException("Replaced exception");
        args.FlowBehavior = FlowBehavior.ThrowException;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ConditionalReturnAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        FlowBehaviourTracker.Events.Add("OnEntry");

        if (args.Arguments[0] is int value && value <= 0)
        {
            args.ReturnValue = -1;
            args.FlowBehavior = FlowBehavior.Return;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ValidationAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        if (args.Arguments[0] is int value && value < 0)
        {
            args.Exception = new ArgumentException("Value must be non-negative");
            args.FlowBehavior = FlowBehavior.ThrowException;
        }
    }
}

// Property interception aspects
[AttributeUsage(AttributeTargets.Property)]
public class PropertyAccessTrackingAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        PropertyAccessTracker.Events.Add("OnGetValue");
        args.ProceedGetValue();
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        PropertyAccessTracker.Events.Add("OnSetValue");
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class PropertyValueCaptureAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
        PropertyValueCapture.GetValue = args.Value;
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        PropertyValueCapture.SetValue = args.Value;
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class GetValueModifierAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
        if (args.Value is int intValue)
        {
            args.Value = intValue * 10;
        }
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class SetValueModifierAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        if (args.Value is int intValue)
        {
            args.Value = intValue * 2;
        }
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class PropertyInfoCaptureAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        PropertyInfoCapture.CapturedArgs = args;
        args.ProceedGetValue();
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        PropertyInfoCapture.CapturedArgs = args;
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class ComputedPropertyAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.Value = 100;
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class ValidationPropertyAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        if (args.Value is int intValue && intValue < 0)
        {
            ValidatedPropertyAspect.LastRejectedValue = intValue;
            return;
        }
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class ChangeTrackerAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        var newValue = args.Value; // Save the new incoming value
        args.ProceedGetValue(); // Get the current (old) value
        ChangeTrackingAspect.OldValue = args.Value; // Save old value

        ChangeTrackingAspect.NewValue = newValue; // Use the saved new value
        args.Value = newValue; // Restore for ProceedSetValue
        args.ProceedSetValue();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class TimingAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        TimingAspect.ExecutionLog.Add("TimingOnEntry");
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        TimingAspect.ExecutionLog.Add("TimingOnExit");
    }
}

// Practical aspects
[AttributeUsage(AttributeTargets.Method)]
public class CacheAttribute : OnMethodBoundaryAspect
{
    private static Dictionary<string, object?> _cache = new();

    internal static void ClearCache() => _cache.Clear();

    public override void OnEntry(MethodExecutionArgs args)
    {
        var key = GenerateCacheKey(args);
        if (_cache.TryGetValue(key, out var cachedValue))
        {
            args.ReturnValue = cachedValue;
            args.FlowBehavior = FlowBehavior.Return;
        }
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        var key = GenerateCacheKey(args);
        _cache[key] = args.ReturnValue;
    }

    private string GenerateCacheKey(MethodExecutionArgs args)
    {
        var argString = string.Join(",", args.Arguments);
        return $"{args.Method.Name}:{argString}";
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class CacheWithExpirationAttribute : OnMethodBoundaryAspect
{
    public int ExpirationSeconds { get; set; } = 60;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class LoggingAttribute : OnMethodBoundaryAspect
{
    private Stopwatch? _stopwatch;

    public override void OnEntry(MethodExecutionArgs args)
    {
        _stopwatch = Stopwatch.StartNew();
        var argString = string.Join(", ", args.Arguments);
        LoggingAspect.Logs.Add($"Entering {args.Method.Name} with arguments: {argString}");
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        _stopwatch?.Stop();
        LoggingAspect.Logs.Add($"Exiting {args.Method.Name} after {_stopwatch?.ElapsedMilliseconds}ms");
    }

    public override void OnException(MethodExecutionArgs args)
    {
        LoggingAspect.Logs.Add($"Exception in {args.Method.Name}: {args.Exception?.GetType().Name} - {args.Exception?.Message}");
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class NotifyPropertyChangedAttribute : LocationInterceptionAspect
{
    public override void OnSetValue(LocationInterceptionArgs args)
    {
        var newValue = args.Value; // Save the new incoming value
        args.ProceedGetValue(); // Get the current (old) value
        var oldValue = args.Value; // Save old value

        if (!Equals(oldValue, newValue))
        {
            NotifyPropertyChangedAspect.LastOldValue = oldValue;
            NotifyPropertyChangedAspect.LastNewValue = newValue;

            args.Value = newValue; // Restore for ProceedSetValue
            args.ProceedSetValue();

            if (args.Instance is INotifyPropertyChanged inpc)
            {
                var propertyName = args.Property.Name;
                RaisePropertyChanged(inpc, propertyName);
            }
        }
    }

    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
    }

    private void RaisePropertyChanged(INotifyPropertyChanged instance, string propertyName)
    {
        var eventDelegate = instance.GetType()
            .GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(instance) as PropertyChangedEventHandler;

        eventDelegate?.Invoke(instance, new PropertyChangedEventArgs(propertyName));

        // Check for dependent properties via AlsoNotify attribute
        var property = instance.GetType().GetProperty(propertyName);
        if (property != null)
        {
            var alsoNotifyAttrs = property.GetCustomAttributes(typeof(AlsoNotifyAttribute), false)
                .Cast<AlsoNotifyAttribute>();
            foreach (var attr in alsoNotifyAttrs)
            {
                eventDelegate?.Invoke(instance, new PropertyChangedEventArgs(attr.PropertyName));
            }
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class RetryAttribute : MethodInterceptionAspect
{
    public int MaxRetries { get; set; } = 3;

    public override void OnInvoke(MethodInterceptionArgs args)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                args.Proceed();
                return; // Success
            }
            catch (Exception ex)
            {
                lastException = ex;
                // Continue to next attempt
            }
        }

        // All retries failed, throw the last exception
        if (lastException != null)
            throw lastException;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class TransactionAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        TransactionAspect.WasCommitted = true;
    }

    public override void OnException(MethodExecutionArgs args)
    {
        TransactionAspect.WasRolledBack = true;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class AuthorizationAttribute : OnMethodBoundaryAspect
{
    public string RequiredRole { get; set; } = "admin";

    public override void OnEntry(MethodExecutionArgs args)
    {
        if (AuthorizationAspect.CurrentUser != RequiredRole)
        {
            args.Exception = new UnauthorizedAccessException($"User does not have required role: {RequiredRole}");
            args.FlowBehavior = FlowBehavior.ThrowException;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ValidatePositiveAttribute : OnMethodBoundaryAspect
{
    public int ParameterIndex { get; set; }

    public override void OnEntry(MethodExecutionArgs args)
    {
        if (args.Arguments[ParameterIndex] is int value && value <= 0)
        {
            args.Exception = new ArgumentException($"Parameter must be positive");
            args.FlowBehavior = FlowBehavior.ThrowException;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ValidateNotNullAttribute : OnMethodBoundaryAspect
{
    public int ParameterIndex { get; set; }

    public override void OnEntry(MethodExecutionArgs args)
    {
        if (args.Arguments[ParameterIndex] == null)
        {
            args.Exception = new ArgumentNullException();
            args.FlowBehavior = FlowBehavior.ThrowException;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class PerformanceMonitoringAttribute : OnMethodBoundaryAspect
{
    private Stopwatch? _stopwatch;
    public int WarningThresholdMs { get; set; } = 100;

    public override void OnEntry(MethodExecutionArgs args)
    {
        _stopwatch = Stopwatch.StartNew();
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        _stopwatch?.Stop();
        PerformanceMonitor.LastExecutionTime = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        PerformanceMonitor.LastMethodName = args.Method.Name;

        if (_stopwatch?.ElapsedMilliseconds > WarningThresholdMs)
        {
            PerformanceMonitor.PerformanceWarningRaised = true;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ExceptionHandlingAttribute : OnMethodBoundaryAspect
{
    public override void OnException(MethodExecutionArgs args)
    {
        ExceptionHandlingAspect.LastException = args.Exception;
        args.FlowBehavior = FlowBehavior.Return;

        if (args.Method is System.Reflection.MethodInfo methodInfo && methodInfo.ReturnType != typeof(void))
        {
            args.ReturnValue = Activator.CreateInstance(methodInfo.ReturnType);
        }
    }
}

// Supporting static classes (keep these for backwards compatibility with tests)
public static class CacheAspect
{
    public static void ClearCache() => CacheAttribute.ClearCache();
}

public static class ArgumentDoublerAspect
{
    public static void Reset() { }
}

public static class TimingAspect
{
    public static List<string> ExecutionLog = new();
}

public static class LoggingAspect
{
    public static List<string> Logs = new();
}

public static class NotifyPropertyChangedAspect
{
    public static object? LastOldValue;
    public static object? LastNewValue;
}

public static class AuthorizationAspect
{
    public static string? CurrentUser;
}

public static class TransactionAspect
{
    public static bool WasCommitted;
    public static bool WasRolledBack;

    public static void Reset()
    {
        WasCommitted = false;
        WasRolledBack = false;
    }
}

public static class ExceptionHandlingAspect
{
    public static Exception? LastException;

    public static void Reset() => LastException = null;
}

public static class PerformanceMonitor
{
    public static TimeSpan LastExecutionTime;
    public static string? LastMethodName;
    public static bool PerformanceWarningRaised;

    public static void Reset()
    {
        LastExecutionTime = TimeSpan.Zero;
        LastMethodName = null;
        PerformanceWarningRaised = false;
    }
}

// ====================================
// Advanced Real-World Aspects
// ====================================

#region Rate Limiting

[AttributeUsage(AttributeTargets.Method)]
public class RateLimitAttribute : OnMethodBoundaryAspect
{
    public int MaxCalls { get; set; }

    public override void OnEntry(MethodExecutionArgs args)
    {
        // Create instance-specific key using instance hash code
        var instanceKey = args.Instance?.GetHashCode() ?? 0;
        var methodKey = $"{instanceKey}:{args.Method.DeclaringType?.FullName}.{args.Method.Name}";
        var currentCount = RateLimiter.GetCount(methodKey);

        if (currentCount >= MaxCalls)
        {
            throw new InvalidOperationException("Rate limit exceeded");
        }
        RateLimiter.Increment(methodKey);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class RateLimitWithTimeWindowAttribute : OnMethodBoundaryAspect
{
    public int MaxCalls { get; set; }
    public int WindowSeconds { get; set; }

    public override void OnEntry(MethodExecutionArgs args)
    {
        // Create instance-specific key using instance hash code
        var instanceKey = args.Instance?.GetHashCode() ?? 0;
        var methodKey = $"{instanceKey}:{args.Method.DeclaringType?.FullName}.{args.Method.Name}";
        var currentCount = RateLimiterWithReset.GetCount(methodKey);

        if (currentCount >= MaxCalls)
        {
            throw new InvalidOperationException("Rate limit exceeded");
        }
        RateLimiterWithReset.Increment(methodKey);
    }
}

public static class RateLimiter
{
    private static ConcurrentDictionary<string, int> _counters = new();

    public static int CallCount => _counters.Values.Sum(); // For backward compat
    public static int GetCount(string key) => _counters.GetOrAdd(key, 0);
    public static void Increment(string key) => _counters.AddOrUpdate(key, 1, (k, v) => v + 1);
    public static void Reset() => _counters.Clear();
}

public static class RateLimiterWithReset
{
    private static ConcurrentDictionary<string, int> _counters = new();

    public static int CallCount => _counters.Values.Sum(); // For backward compat
    public static int GetCount(string key) => _counters.GetOrAdd(key, 0);
    public static void Increment(string key) => _counters.AddOrUpdate(key, 1, (k, v) => v + 1);
    public static void Reset() => _counters.Clear();
    public static void SimulateTimeElapsed() => _counters.Clear();
}

#endregion

#region Circuit Breaker

[AttributeUsage(AttributeTargets.Method)]
public class CircuitBreakerAttribute : OnMethodBoundaryAspect
{
    public int FailureThreshold { get; set; }

    public override void OnEntry(MethodExecutionArgs args)
    {
        if (CircuitBreaker.IsOpen)
        {
            throw new InvalidOperationException("Circuit breaker is open");
        }
    }

    public override void OnException(MethodExecutionArgs args)
    {
        CircuitBreaker.FailureCount++;
        if (CircuitBreaker.FailureCount >= FailureThreshold)
        {
            CircuitBreaker.IsOpen = true;
        }
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        CircuitBreaker.FailureCount = 0;
        CircuitBreaker.IsOpen = false;
    }
}

public static class CircuitBreaker
{
    public static bool IsOpen;
    public static int FailureCount;

    public static void Reset()
    {
        IsOpen = false;
        FailureCount = 0;
    }

    public static void SimulateTimeout()
    {
        // In real implementation, would check time elapsed
        // For testing, just allow next call
        IsOpen = false;
    }
}

#endregion

#region Memoization

[AttributeUsage(AttributeTargets.Method)]
public class MemoizeAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        var key = MemoizationCache.GetKey(args.Method, args.Arguments);
        if (MemoizationCache.TryGet(key, out var cachedValue))
        {
            args.ReturnValue = cachedValue;
            args.FlowBehavior = FlowBehavior.Return;
        }
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        var key = MemoizationCache.GetKey(args.Method, args.Arguments);
        MemoizationCache.Set(key, args.ReturnValue);
    }
}

public static class MemoizationCache
{
    private static readonly Dictionary<string, object?> _cache = new();

    public static string GetKey(System.Reflection.MethodBase method, Arguments args)
    {
        var parts = new List<string> { method.Name };
        for (int i = 0; i < args.Count; i++)
        {
            parts.Add(args[i]?.ToString() ?? "null");
        }
        return string.Join(":", parts);
    }

    public static bool TryGet(string key, out object? value)
    {
        return _cache.TryGetValue(key, out value);
    }

    public static void Set(string key, object? value)
    {
        _cache[key] = value;
    }

    public static void Clear() => _cache.Clear();
}

#endregion

#region Dirty Tracking

[AttributeUsage(AttributeTargets.Property)]
public class DirtyTrackingAttribute : LocationInterceptionAspect
{
    public override void OnSetValue(LocationInterceptionArgs args)
    {
        var newValue = args.Value;
        args.ProceedGetValue();
        var oldValue = args.Value;

        if (!Equals(oldValue, newValue))
        {
            DirtyTracker.MarkDirty(args.Property.Name);
        }

        args.Value = newValue;
        args.ProceedSetValue();
    }
}

public static class DirtyTracker
{
    public static HashSet<string> DirtyProperties = new();

    public static bool IsDirty(string propertyName) => DirtyProperties.Contains(propertyName);
    public static void MarkDirty(string propertyName) => DirtyProperties.Add(propertyName);
    public static void ClearDirty() => DirtyProperties.Clear();
    public static void Reset() => DirtyProperties.Clear();
}

#endregion

#region Audit Logging

[AttributeUsage(AttributeTargets.Method)]
public class AuditLogAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        var entry = new AuditLogEntry
        {
            MethodName = args.Method.Name,
            User = AuditLog.CurrentUser,
            Timestamp = DateTime.UtcNow,
            Arguments = string.Join(", ", args.Arguments.Select(a => a?.ToString() ?? "null"))
        };
        AuditLog.CurrentEntry = entry;
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        if (AuditLog.CurrentEntry != null)
        {
            AuditLog.CurrentEntry.ReturnValue = args.ReturnValue?.ToString();
            AuditLog.Entries.Add(AuditLog.CurrentEntry);
        }
    }
}

public class AuditLogEntry
{
    public string MethodName { get; set; } = "";
    public string User { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Arguments { get; set; } = "";
    public string? ReturnValue { get; set; }
}

public static class AuditLog
{
    public static List<AuditLogEntry> Entries = new();
    public static string CurrentUser = "";
    public static AuditLogEntry? CurrentEntry;

    public static void Clear()
    {
        Entries.Clear();
        CurrentEntry = null;
    }
}

#endregion

#region Dependent Property Notification

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class AlsoNotifyAttribute : Attribute
{
    public string PropertyName { get; }

    public AlsoNotifyAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }
}

#endregion

#region Validation

[AttributeUsage(AttributeTargets.Parameter)]
public class ValidateEmailAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Parameter)]
public class ValidateRangeAttribute : Attribute
{
    public int Min { get; set; }
    public int Max { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public class ValidateParametersAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        var method = args.Method;
        var parameters = method.GetParameters();

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var value = args.Arguments[i];

            // Check email validation
            var emailAttr = param.GetCustomAttributes(typeof(ValidateEmailAttribute), false)
                .Cast<ValidateEmailAttribute>()
                .FirstOrDefault();
            if (emailAttr != null && value is string email)
            {
                if (!email.Contains("@") || !email.Contains("."))
                {
                    throw new ArgumentException($"Invalid email: {email}");
                }
            }

            // Check range validation
            var rangeAttr = param.GetCustomAttributes(typeof(ValidateRangeAttribute), false)
                .Cast<ValidateRangeAttribute>()
                .FirstOrDefault();
            if (rangeAttr != null && value is int intValue)
            {
                if (intValue < rangeAttr.Min || intValue > rangeAttr.Max)
                {
                    throw new ArgumentException(
                        $"Value {intValue} is out of range [{rangeAttr.Min}, {rangeAttr.Max}]");
                }
            }
        }
    }
}

#endregion
