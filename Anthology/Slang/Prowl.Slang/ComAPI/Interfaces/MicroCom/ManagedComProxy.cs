using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


// Marshals a managed class to a compatible native COM pointer
[StructLayout(LayoutKind.Sequential)]
public unsafe class ManagedComProxy<T> : IUnknown where T : IUnknown
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


    private GCHandle _handle;
    private uint _refCount;


    public uint AddRef()
    {
        if (_refCount++ == 0 && !_handle.IsAllocated)
            _handle = GCHandle.Alloc(this);

        return _refCount;
    }


    public SlangResult QueryInterface(ref Guid uuid, out nint obj)
    {
        if (s_interfaceGuids.Contains(uuid))
        {
            obj = (nint)ProxyVTable;
            return SlangResult.Ok;
        }

        obj = 0;
        return SlangResult.NoInterface;
    }


    public uint Release()
    {
        if (--_refCount == 0 && _handle.IsAllocated)
        {
            _handle.Free();
            ReleaseNativeResources();
        }

        return _refCount;
    }


    private void ReleaseNativeResources()
    {
        if (_proxyVTable == null)
            return;

        ProxyEmitter.FreeManagedProxyVTable(_proxyVTable);
        _proxyVTable = null;
    }


    private ProxyVTable* _proxyVTable = null;

    public ProxyVTable* ProxyVTable
    {
        get
        {
            if (_proxyVTable == null)
            {
                if (!_handle.IsAllocated)
                    _handle = GCHandle.Alloc(this);

                _proxyVTable = ProxyEmitter.CreateManagedProxyVTable<T>(_handle);
            }

            return _proxyVTable;
        }
    }


    public T* NativeRef => (T*)ProxyVTable;


    public static implicit operator T*(ManagedComProxy<T> src)
    {
        return src.NativeRef;
    }
}
