// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ProxyVTable
{
    public nint* VTable;
    public void* ManagedHandle;
}


internal unsafe interface IManagedProxyThunks<T> where T : IUnknown
{
    static abstract int SlotCount { get; }
    static abstract void FillVTable(void** vtable);
}


internal static unsafe class ManagedProxyHelpers
{
    public static object GetTarget(ProxyVTable* vtable) => GCHandle.FromIntPtr((nint)vtable->ManagedHandle).Target!;


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static uint AddRef(ProxyVTable* self) => ((ManagedComProxyBase)GetTarget(self)).AddRef();


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static uint Release(ProxyVTable* self) => ((ManagedComProxyBase)GetTarget(self)).Release();
}


// Marshals a managed class to a compatible native COM pointer
internal abstract unsafe class ManagedComProxyBase
{
    private GCHandle _handle;
    private uint _refCount;
    private ProxyVTable* _proxyVTable;


    protected abstract int SlotCount { get; }
    protected abstract void FillVTable(void** vtable);


    public uint AddRef()
    {
        if (_refCount++ == 0 && !_handle.IsAllocated)
            _handle = GCHandle.Alloc(this);

        return _refCount;
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

        NativeMemory.Free(_proxyVTable->VTable);
        NativeMemory.Free(_proxyVTable);
        _proxyVTable = null;
    }


    protected ProxyVTable* ProxyVTablePtr
    {
        get
        {
            if (_proxyVTable == null)
            {
                if (!_handle.IsAllocated)
                    AddRef();

                ProxyVTable* proxy = (ProxyVTable*)NativeMemory.Alloc((nuint)sizeof(ProxyVTable));

                proxy->VTable = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * SlotCount));
                proxy->ManagedHandle = (void*)GCHandle.ToIntPtr(_handle);

                FillVTable((void**)proxy->VTable);

                _proxyVTable = proxy;
            }

            return _proxyVTable;
        }
    }
}


internal abstract unsafe class ManagedComProxy<T, TThunks> : ManagedComProxyBase, IUnknown
    where T : IUnknown
    where TThunks : IManagedProxyThunks<T>
{
    protected override int SlotCount => TThunks.SlotCount;
    protected override void FillVTable(void** vtable) => TThunks.FillVTable(vtable);


    public T* NativeRef => (T*)ProxyVTablePtr;


    public static implicit operator T*(ManagedComProxy<T, TThunks> src) => src.NativeRef;


    SlangResult IUnknown.QueryInterface(ref Guid uuid, out nint obj)
    {
        var fn = (delegate* unmanaged[Cdecl]<ProxyVTable*, ref Guid, out nint, SlangResult>)ProxyVTablePtr->VTable[0];
        return fn(ProxyVTablePtr, ref uuid, out obj);
    }


    uint IUnknown.AddRef() => AddRef();
    uint IUnknown.Release() => Release();
}
