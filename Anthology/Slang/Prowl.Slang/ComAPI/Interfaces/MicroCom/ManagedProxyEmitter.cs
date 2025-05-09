using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct ProxyVTable
{
    public nint* VTable;
    public void* ManagedHandle;
}


public static partial class ProxyEmitter
{
    private static Dictionary<MethodInfo, MethodInfo> s_staticProxyMethods = [];


    public static MethodInfo GetProxyMethod(MethodInfo method)
    {
        if (!s_staticProxyMethods.TryGetValue(method, out MethodInfo? methodInfo))
            s_staticProxyMethods[method] = methodInfo = BuildManagedProxyMethod(method);

        return methodInfo;
    }


    private static string GetProxyMethodClassName(MethodInfo method)
    {
#if DEBUG
        return $"{method.DeclaringType!.Name}_{method.Name}_Proxy";
#else
        return $"__DynamicImpl__{method.DeclaringType!.Name}__{method.Name}__Proxy__";
#endif
    }


    private static string GetProxyMethodName()
    {
        return "__Interop__ProxyMain__";
    }


    public static unsafe object GetTarget(ProxyVTable* vtable)
    {
        return GCHandle.FromIntPtr((nint)vtable->ManagedHandle).Target!;
    }


    private static MethodInfo BuildManagedProxyMethod(MethodInfo managedMethod)
    {
        Type vtableType = typeof(ProxyVTable);
        MethodInfo getTarget = typeof(ProxyEmitter).GetMethod(nameof(GetTarget), BindingFlags.Static | BindingFlags.Public)!;

        ParameterInfo[] parameters = managedMethod.GetParameters();

        Type[] paramTypes = parameters.Select(p => p.ParameterType).ToArray();

        // Define the proxy method signature
        Type[] methodArgs = [typeof(ProxyVTable*), .. paramTypes];

        TypeBuilder builder = ModuleBuilder.DefineType(GetProxyMethodClassName(managedMethod), TypeAttributes.Public, null, null);
        MethodBuilder methodBuilder = builder.DefineMethod(
            GetProxyMethodName(),
            MethodAttributes.Public | MethodAttributes.Static,
            managedMethod.ReturnType,
            methodArgs);

        // Get the ILGenerator for the method
        ILGenerator il = methodBuilder.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getTarget);
        il.Emit(OpCodes.Castclass, managedMethod.DeclaringType!);

        // Push all arguments (skip arg0 = vtable)
        for (int i = 0; i < paramTypes.Length; i++)
            il.Emit(OpCodes.Ldarg, i + 1);

        il.Emit(OpCodes.Callvirt, managedMethod);
        il.Emit(OpCodes.Ret);

        MethodInfo generatedMethod = builder.CreateType().GetMethod(GetProxyMethodName(), BindingFlags.Static | BindingFlags.Public)!;

        RuntimeHelpers.PrepareMethod(generatedMethod.MethodHandle);

        return generatedMethod;
    }


    public static unsafe ProxyVTable* CreateManagedProxyVTable<T>(GCHandle managedHandle) where T : IUnknown
    {
        ValidateInterface<T>();

        List<MethodInfo> tree = GetMethodTree<T>();

        ProxyVTable* proxy = (ProxyVTable*)NativeMemory.Alloc((nuint)sizeof(ProxyVTable));
        nint* nativeVtablePtr = (nint*)NativeMemory.Alloc((nuint)(nint.Size * tree.Count));

        proxy->VTable = nativeVtablePtr;
        proxy->ManagedHandle = (void*)GCHandle.ToIntPtr(managedHandle);

        List<MethodInfo> methods = new();

        for (int i = 0; i < tree.Count; i++)
        {
            proxy->VTable[i] = GetProxyMethod(tree[i]).MethodHandle.GetFunctionPointer();
        }

        return proxy;
    }


    public static unsafe void FreeManagedProxyVTable(ProxyVTable* proxy)
    {
        NativeMemory.Free(proxy->VTable);
        NativeMemory.Free(proxy);
    }
}
