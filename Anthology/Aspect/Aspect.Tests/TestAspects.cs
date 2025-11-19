using Aspect;
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

// Inheritance test aspects
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class InheritableAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        InheritanceTracker.InterceptedMethods.Add(args.Method.Name);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class NonInheritableAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        InheritanceTracker.InterceptedMethods.Add(args.Method.Name);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SecondaryAspectAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        SecondaryTracker.InterceptedMethods.Add(args.Method.Name);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class OverrideAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        OverrideTracker.InterceptedMethods.Add(args.Method.Name);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class HighPriorityAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        PriorityTracker.ExecutionOrder.Add("HighPriority");
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class LowPriorityAttribute : OnMethodBoundaryAspect
{
    public override void OnSuccess(MethodExecutionArgs args)
    {
        PriorityTracker.ExecutionOrder.Add("LowPriority");
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class TargetMemberFilterAttribute : OnMethodBoundaryAspect
{
    public string TargetMembers { get; set; } = "Included*";

    public override void OnEntry(MethodExecutionArgs args)
    {
        if (args.Method.Name.StartsWith("Included"))
        {
            InheritanceTracker.InterceptedMethods.Add(args.Method.Name);
        }
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
