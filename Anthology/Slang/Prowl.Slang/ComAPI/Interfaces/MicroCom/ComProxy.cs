using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;

public abstract class ComProxy : IUnknown
{
    protected IntPtr _comPtr;


    public ComProxy(IntPtr nativePtr)
    {
        _comPtr = nativePtr;
        AddRef();
    }


    public unsafe T As<T>() where T : IUnknown
    {
        Guid guid = UUIDAttribute.GetGuid<T>();

        QueryInterface(ref guid, out nint objPtr).Throw();

        return ProxyEmitter.CreateProxy((T*)objPtr);
    }


    public unsafe bool TryAs<T>(out T? value) where T : IUnknown
    {
        Guid guid = UUIDAttribute.GetGuid<T>();

        bool isOK = QueryInterface(ref guid, out nint objPtr).IsOk();

        value = isOK ? ProxyEmitter.CreateProxy((T*)objPtr) : default;

        return isOK;
    }


    public abstract uint AddRef();
    public abstract SlangResult QueryInterface(ref Guid uuid, out nint obj);
    public abstract uint Release();


    ~ComProxy()
    {
        Release();
    }
}
