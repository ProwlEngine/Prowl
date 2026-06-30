using System.Reflection;

namespace Aspect;

/// <summary>
/// Base class for aspects that intercept property access (get/set).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public abstract class LocationInterceptionAspect : Attribute
{
    /// <summary>
    /// Gets or sets the aspect priority. Higher priority aspects execute first.
    /// Default is 0.
    /// </summary>
    public int AspectPriority { get; set; } = 0;

    /// <summary>
    /// Method executed when the property value is read.
    /// Must call args.ProceedGetValue() to retrieve the actual value, unless providing a computed value.
    /// </summary>
    /// <param name="args">Contextual information about the property access</param>
    public virtual void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
    }

    /// <summary>
    /// Method executed when the property value is written.
    /// Must call args.ProceedSetValue() to actually set the value, unless skipping the set.
    /// </summary>
    /// <param name="args">Contextual information about the property access</param>
    public virtual void OnSetValue(LocationInterceptionArgs args)
    {
        args.ProceedSetValue();
    }
}
