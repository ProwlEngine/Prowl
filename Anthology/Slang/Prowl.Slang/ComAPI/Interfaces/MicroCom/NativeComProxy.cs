using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


internal static class ReferenceCounter
{
    private static HashSet<object?> s_referenceTracker = new();


    public static uint UpdateRef<T>(T obj, uint value)
    {
        if (value < 1)
            s_referenceTracker.Remove(obj);
        else
            s_referenceTracker.Add(obj);

        return value;
    }
}


[StructLayout(LayoutKind.Sequential)]
public unsafe struct ProxyVTable
{
    public void* VTable;           // Function pointers for native calls
    public void* ManagedVTable;    // Function pointers for managed methods
    public void* ManagedThis;      // GCHandle or raw object handle
}


[StructLayout(LayoutKind.Sequential)]
public unsafe class NativeComProxy<T> : IUnknown where T : IUnknown
{
    private static HashSet<Guid> s_interfaceGuids = GetInterfaces();

    private static HashSet<Guid> GetInterfaces()
    {
        HashSet<Guid> guids = new();

        Type? type = typeof(T);

        while (type != null)
        {
            guids.Add(UUIDAttribute.GetGuid(type));
            type = type.GetInterfaces().FirstOrDefault();
        }

        return guids;
    }

    private uint _refCount;

    private T* _nativeInterfacePtr = null;

    public T* NativeInterfacePtr
    {
        get
        {
            if (_nativeInterfacePtr == null)
                _nativeInterfacePtr = ProxyEmitter.CreateNativeProxy(this);

            return _nativeInterfacePtr;
        }

        internal set => _nativeInterfacePtr = value;
    }

    public uint AddRef() => ReferenceCounter.UpdateRef(this, ++_refCount);

    public SlangResult QueryInterface(ref Guid uuid, out nint obj)
    {
        if (s_interfaceGuids.Contains(uuid))
        {
            obj = (nint)NativeInterfacePtr;
            return new SlangResult();
        }

        obj = 0;
        return new SlangResult(0x80004002);
    }

    public uint Release() => ReferenceCounter.UpdateRef(this, --_refCount);


    ~NativeComProxy()
    {
        ProxyEmitter.FreeNativeProxy(this);
    }
}
