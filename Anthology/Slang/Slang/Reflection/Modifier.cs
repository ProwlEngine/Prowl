using System;

namespace Prowl.Slang;


public unsafe struct Modifier
{
    internal ComponentType _component;
    internal Native.Modifier* _ptr;


    internal Modifier(Native.Modifier* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }
}
