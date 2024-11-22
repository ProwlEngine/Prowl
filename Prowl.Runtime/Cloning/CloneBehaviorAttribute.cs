using System;

namespace Prowl.Runtime.Cloning
{
    /// <summary>
    /// Specifies the cloning behavior of a certain class, struct or field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Field, AllowMultiple = false)]
	public class CloneBehaviorAttribute(Type targetType, CloneBehavior behavior) : Attribute
	{
        public Type TargetType { get; } = targetType;
        public CloneBehavior Behavior { get; } = behavior;

        public CloneBehaviorAttribute(CloneBehavior behavior) : this(null, behavior) {}
    }
}
