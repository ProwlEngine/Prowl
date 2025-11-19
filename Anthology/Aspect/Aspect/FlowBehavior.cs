namespace Aspect;

/// <summary>
/// Specifies how the execution flow should proceed after an aspect method.
/// </summary>
public enum FlowBehavior
{
    /// <summary>
    /// Continue normal execution.
    /// In OnEntry: Execute the method.
    /// In OnException: Rethrow the exception.
    /// </summary>
    Continue,

    /// <summary>
    /// Return from the method without executing it (in OnEntry) or suppress the exception (in OnException).
    /// The ReturnValue property will be used as the method's return value.
    /// OnSuccess will not be called, but OnExit will still be called.
    /// </summary>
    Return,

    /// <summary>
    /// Throw an exception.
    /// The Exception property must be set before using this flow behavior.
    /// OnException will be called with the specified exception.
    /// </summary>
    ThrowException
}
