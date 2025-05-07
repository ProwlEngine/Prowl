using System;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SlangResult
{
    int _value;

    public readonly void Throw()
    {
        Exception? ex = Marshal.GetExceptionForHR(_value);

        if (ex != null)
            throw ex;
    }
}
