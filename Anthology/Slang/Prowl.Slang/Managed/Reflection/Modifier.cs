using System;

namespace Prowl.Slang;


public unsafe struct Modifier
{
    internal Session _session;
    internal Native.Modifier* _ptr;


    internal Modifier(Native.Modifier* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }
}