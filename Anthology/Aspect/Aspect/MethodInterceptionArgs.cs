using System.Reflection;

namespace Aspect;

/// <summary>
/// Provides context for method interception aspects.
/// </summary>
public class MethodInterceptionArgs
{
    /// <summary>
    /// Gets or sets the method being intercepted.
    /// </summary>
    public MethodBase Method { get; set; } = null!;

    /// <summary>
    /// Gets or sets the instance on which the method is being called (null for static methods).
    /// </summary>
    public object? Instance { get; set; }

    /// <summary>
    /// Gets or sets the arguments passed to the method.
    /// </summary>
    public Arguments Arguments { get; set; } = null!;

    /// <summary>
    /// Gets or sets the return value of the method.
    /// </summary>
    public object? ReturnValue { get; set; }

    /// <summary>
    /// Delegate that executes the original method.
    /// This delegate should extract arguments from Arguments, call the original method,
    /// and store the return value in ReturnValue.
    /// </summary>
    public Action<MethodInterceptionArgs>? ProceedDelegate { get; set; }

    /// <summary>
    /// Executes the original method.
    /// </summary>
    public void Proceed()
    {
        if (ProceedDelegate == null)
            throw new InvalidOperationException("Proceed delegate is not set");

        ProceedDelegate(this);
    }
}
