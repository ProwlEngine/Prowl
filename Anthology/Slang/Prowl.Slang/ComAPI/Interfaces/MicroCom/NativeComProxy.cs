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


    public abstract uint AddRef();
    public abstract SlangResult QueryInterface(ref Guid uuid, out nint obj);
    public abstract uint Release();


    ~NativeComProxy()
    {
        Release();
    }
}
