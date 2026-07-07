// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Slang.Native;


// Marshals invocations on a managed object to a native COM Vtable
internal abstract unsafe class NativeComProxy(void* ptr, bool trackReferences = true)
{
    public IntPtr ComPtr => (nint)_ptr;

    protected readonly void* _ptr = ptr;
    private readonly bool _trackReferences = trackReferences;


    protected void** VTable => *(void***)_ptr;


    public SlangResult QueryInterface(ref Guid uuid, out nint obj)
    {
        var fn = (delegate* unmanaged[Cdecl]<void*, ref Guid, out nint, SlangResult>)VTable[0];
        return fn(_ptr, ref uuid, out obj);
    }


    public uint AddRef()
    {
        var fn = (delegate* unmanaged[Cdecl]<void*, uint>)VTable[1];
        return fn(_ptr);
    }


    public uint Release()
    {
        var fn = (delegate* unmanaged[Cdecl]<void*, uint>)VTable[2];
        return fn(_ptr);
    }


    public static bool operator ==(NativeComProxy a, NativeComProxy b)
    {
        return a._ptr == b._ptr;
    }


    public static bool operator !=(NativeComProxy a, NativeComProxy b)
    {
        return a._ptr != b._ptr;
    }


    public override bool Equals(object? obj)
    {
        if (obj is NativeComProxy proxy)
            return proxy._ptr == _ptr;

        if (obj is IntPtr ptr)
            return ptr == ComPtr;

        return false;
    }


    public override int GetHashCode() => ComPtr.GetHashCode();


    ~NativeComProxy()
    {
        if (_trackReferences && _ptr != null)
            Release();
    }
}
