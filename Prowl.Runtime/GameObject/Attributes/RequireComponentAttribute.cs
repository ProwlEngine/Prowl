using System;

/// <summary>
/// Specifies that a GameObject must have certain components in addition to the one where this attribute is applied.
/// </summary>
/// <remarks>
/// When this attribute is used on a component, the Prowl Game Engine will automatically add the required components
/// to the GameObject if they are not already present. This ensures that the component has all its dependencies met.
/// 
/// This attribute can only be applied to classes (typically components deriving from MonoBehaviour).
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class RequireComponentAttribute : Attribute
{
    /// <summary>
    /// Gets the array of component types that are required by this component.
    /// </summary>
    public Type[] types { get; }

    /// <summary>
    /// Initializes a new instance of the RequireComponentAttribute class.
    /// </summary>
    /// <param name="types">An array of Type objects representing the required components.</param>
    /// <example>
    /// [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    /// public class MyPhysicsComponent : MonoBehaviour
    /// {
    ///     // This component requires both a Rigidbody and a Collider
    /// }
    /// </example>
    public RequireComponentAttribute(params Type[] types)
    {
        this.types = types;
    }
}
