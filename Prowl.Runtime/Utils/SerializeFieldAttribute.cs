using System;

namespace Prowl.Runtime.Utils
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SerializeFieldAttribute : Attribute
    {
    }
}
