using System.Reflection;

namespace Aspect;

/// <summary>
/// Provides contextual information about method execution to aspect methods.
/// </summary>
public class MethodExecutionArgs
{
    /// <summary>
    /// Gets the method being executed.
    /// </summary>
    public MethodBase Method { get; set; } = null!;

    /// <summary>
    /// Gets or sets the instance on which the method is being executed.
    /// Null for static methods.
    /// </summary>
    public object? Instance { get; set; }

    /// <summary>
    /// Gets the arguments passed to the method.
    /// Arguments can be modified in OnEntry to change what the method receives.
    /// </summary>
    public Arguments Arguments { get; set; } = null!;

    /// <summary>
    /// Gets or sets the return value of the method.
    /// Can be set in OnEntry (with FlowBehavior.Return) to skip method execution.
    /// Can be modified in OnSuccess to change the return value.
    /// </summary>
    public object? ReturnValue { get; set; }

    /// <summary>
    /// Gets or sets the exception that occurred during method execution.
    /// Can be modified in OnException to replace the exception.
    /// Can be set in OnEntry with FlowBehavior.ThrowException to throw before method execution.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets or sets the flow behavior that controls execution.
    /// Default is Continue.
    /// </summary>
    public FlowBehavior FlowBehavior { get; set; } = FlowBehavior.Continue;

    /// <summary>
    /// Gets the type information for the method being executed.
    /// </summary>
    public MethodInfo MethodInfo => (MethodInfo)Method;
}
