using System;

namespace Prowl.Runtime;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ExecuteAlwaysAttribute : Attribute
{
    public ExecuteAlwaysAttribute()
    {
    }
}
