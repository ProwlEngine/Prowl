namespace Aspect;

/// <summary>
/// Base class for method interception aspects.
/// Allows complete control over method execution through the Proceed() pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public abstract class MethodInterceptionAspect : Attribute
{
    /// <summary>
    /// Called instead of the method being intercepted.
    /// Call args.Proceed() to execute the original method.
    /// </summary>
    /// <param name="args">Context containing method information and the Proceed() method</param>
    public abstract void OnInvoke(MethodInterceptionArgs args);
}
