using System;

namespace Prowl.Slang.NativeAPI;


// Marshals invocations on a managed object to a native COM Vtable
public abstract class NativeComProxy : IUnknown
{
    protected IntPtr _comPtr;


    public NativeComProxy(IntPtr nativePtr)
    {
        _comPtr = nativePtr;
        AddRef();
    }


    public static unsafe T Create<T>(T* ptr) where T : IUnknown
    {
        return ProxyEmitter.CreateNativeProxy(ptr);
    }


    public unsafe T As<T>() where T : IUnknown
    {
        Guid guid = UUIDAttribute.GetGuid<T>();

        QueryInterface(ref guid, out nint objPtr).Throw();

        return ProxyEmitter.CreateNativeProxy((T*)objPtr);
    }


    public unsafe bool TryAs<T>(out T? value) where T : IUnknown
    {
        Guid guid = UUIDAttribute.GetGuid<T>();

        bool isOK = QueryInterface(ref guid, out nint objPtr).IsOk();

        value = isOK ? ProxyEmitter.CreateNativeProxy((T*)objPtr) : default;

        return isOK;
    }


    public static bool operator ==(NativeComProxy a, NativeComProxy b)
    {
        return a._comPtr == b._comPtr;
    }


    public static bool operator !=(NativeComProxy a, NativeComProxy b)
    {
        return a._comPtr != b._comPtr;
    }


    public override bool Equals(object? obj)
    {
        if (obj is NativeComProxy proxy)
            return proxy._comPtr == _comPtr;

        if (obj is IntPtr ptr)
            return ptr == _comPtr;

        return false;
    }


    public override int GetHashCode() => _comPtr.ToInt32();


    public abstract uint AddRef();
    public abstract SlangResult QueryInterface(ref Guid uuid, out nint obj);
    public abstract uint Release();


    ~NativeComProxy()
    {
        Release();
    }
}
