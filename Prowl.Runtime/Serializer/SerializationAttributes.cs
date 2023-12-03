using System;

namespace Prowl.Runtime.Serialization
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class IgnoreOnNullAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SerializeIgnoreAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = false)]
    public class SerializeAsAttribute : Attribute
    {
        public string Name { get; set; }
        public SerializeAsAttribute(string name) => Name = name;
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = true)]
    public class FormerlySerializedAsAttribute : Attribute
    {
        public string Name { get; set; }
        public FormerlySerializedAsAttribute(string name) => Name = name;
    }
}
