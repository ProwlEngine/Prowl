using System.Reflection;

namespace Aspect;

/// <summary>
/// Base class for aspects that intercept method execution.
/// Provides hooks for OnEntry, OnSuccess, OnException, and OnExit.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = true)]
public abstract class OnMethodBoundaryAspect : Attribute
{
    /// <summary>
    /// Gets or sets the aspect priority. Higher priority aspects execute first.
    /// Default is 0.
    /// </summary>
    public int AspectPriority { get; set; } = 0;

    /// <summary>
    /// Method executed before the target method.
    /// Can modify arguments or change execution flow.
    /// </summary>
    /// <param name="args">Contextual information about the method execution</param>
    public virtual void OnEntry(MethodExecutionArgs args)
    {
    }

    /// <summary>
    /// Method executed after the target method completes successfully (without exception).
    /// Can modify the return value.
    /// </summary>
    /// <param name="args">Contextual information about the method execution</param>
    public virtual void OnSuccess(MethodExecutionArgs args)
    {
    }

    /// <summary>
    /// Method executed when the target method throws an exception.
    /// Can suppress or replace the exception.
    /// </summary>
    /// <param name="args">Contextual information about the method execution</param>
    public virtual void OnException(MethodExecutionArgs args)
    {
    }

    /// <summary>
    /// Method executed after the target method, regardless of success or failure.
    /// Similar to a finally block.
    /// </summary>
    /// <param name="args">Contextual information about the method execution</param>
    public virtual void OnExit(MethodExecutionArgs args)
    {
    }
}
