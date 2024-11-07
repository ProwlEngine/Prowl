using System;

namespace Prowl.Runtime.Cloning
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public class CloneFieldAttribute(CloneFieldFlags flags) : Attribute
	{
        public CloneFieldFlags Flags { get; } = flags;
    }
}
