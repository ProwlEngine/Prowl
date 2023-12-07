using System;

namespace Prowl.Runtime
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SerializeFieldAttribute : Attribute
    {
    }
}
