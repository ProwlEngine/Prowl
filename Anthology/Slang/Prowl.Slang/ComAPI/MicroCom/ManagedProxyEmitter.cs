using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Slang.NativeAPI;


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
        {
            BuildManagedProxyMethods(method.DeclaringType!);
            methodInfo = s_staticProxyMethods[method];
        }

        return methodInfo;
    }


    private static string GetManagedProxyName(Type type)
    {
#if DEBUG
        return $"{type.Name}ManagedProxy";
#else
        return $"__DynamicImpl__{type.Name}__ManagedProxy__";
#endif
    }


    public static unsafe object GetTarget(ProxyVTable* vtable)
    {
        return GCHandle.FromIntPtr((nint)vtable->ManagedHandle).Target!;
    }


    private static void BuildManagedProxyMethods(Type declaringType)
    {
        TypeBuilder typeBuilder = ModuleBuilder.DefineType(GetManagedProxyName(declaringType), TypeAttributes.Public | TypeAttributes.Sealed);

        MethodInfo[] typeMethods = declaringType.GetMethods();

        for (int i = 0; i < typeMethods.Length; i++)
            BuildManagedProxyMethod(typeBuilder, typeMethods[i]);

        Type createdType = typeBuilder.CreateType();

        for (int i = 0; i < typeMethods.Length; i++)
        {
            MethodInfo generatedMethod = createdType.GetMethod(typeMethods[i].Name, BindingFlags.Static | BindingFlags.Public)!;
            RuntimeHelpers.PrepareMethod(generatedMethod.MethodHandle);

            s_staticProxyMethods[typeMethods[i]] = generatedMethod;
        }
    }


    private static void BuildManagedProxyMethod(TypeBuilder builder, MethodInfo managedMethod)
    {
        Type vtableType = typeof(ProxyVTable);
        MethodInfo getTarget = typeof(ProxyEmitter).GetMethod(nameof(GetTarget), BindingFlags.Static | BindingFlags.Public)!;

        ParameterInfo[] parameters = managedMethod.GetParameters();

        Type[] paramTypes = parameters.Select(p => p.ParameterType).ToArray();

        // Define the proxy method signature
        Type[] methodArgs = [typeof(ProxyVTable*), .. paramTypes];

        MethodBuilder methodBuilder = builder.DefineMethod(
            managedMethod.Name,
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
    }


    public static unsafe ProxyVTable* CreateManagedProxyVTable<T>(GCHandle managedHandle) where T : IUnknown
    {
        ValidateInterface<T>();

        List<MethodInfo> tree = GetMethodTree(typeof(T));

        ProxyVTable* proxy = (ProxyVTable*)NativeMemory.Alloc((nuint)sizeof(ProxyVTable));

        proxy->VTable = (nint*)NativeMemory.Alloc((nuint)(nint.Size * tree.Count));
        proxy->ManagedHandle = (void*)GCHandle.ToIntPtr(managedHandle);

        for (int i = 0; i < tree.Count; i++)
            proxy->VTable[i] = GetProxyMethod(tree[i]).MethodHandle.GetFunctionPointer();

        return proxy;
    }


    public static unsafe void FreeManagedProxyVTable(ProxyVTable* proxy)
    {
        NativeMemory.Free(proxy->VTable);
        NativeMemory.Free(proxy);
    }
}
