using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;

public unsafe class ComObject<T> where T : IUnknown
{
    public ComObject(T* nativePtr)
    {

    }
}