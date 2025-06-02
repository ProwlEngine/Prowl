// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


internal static partial class ProxyEmitter
{
    private static ConcurrentDictionary<Type, Type> s_nativeProxyCache = [];


    public static unsafe T CreateNativeProxy<T>(T* nativeInterfacePtr, bool releaseOnFinalizer = true) where T : IUnknown
    {
        ValidateInterface<T>();

        NativeComProxy proxy = (Activator.CreateInstance(GetNativeProxyType<T>()) as NativeComProxy)!;

        proxy.Initialize((nint)nativeInterfacePtr, releaseOnFinalizer);

        return (T)(object)proxy;
    }


    public static Type GetNativeProxyType<T>() where T : IUnknown
    {
        ValidateInterface<T>();

        if (!s_nativeProxyCache.TryGetValue(typeof(T), out Type? proxyType))
        {
            s_nativeProxyCache[typeof(T)] = proxyType = CreateNativeProxyType(typeof(T));
        }

        return proxyType;
    }


    private static MethodBuilder BuildNativeProxyMethod(TypeBuilder builder, MethodInfo interfaceMethod, FieldInfo ptrField, int vtableIndex)
    {
        ParameterInfo[] parameters = interfaceMethod.GetParameters();

        Type[] paramTypes = parameters.Select(p => p.ParameterType).ToArray();

        MethodBuilder methodBuilder = builder.DefineMethod(
            interfaceMethod.Name,
            MethodAttributes.Public | MethodAttributes.Virtual,
            interfaceMethod.ReturnType,
            paramTypes
        );

        ILGenerator il = methodBuilder.GetILGenerator();

        // Load calli args onto stack
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ptrField);

        for (int i = 0; i < paramTypes.Length; i++)
            il.Emit(OpCodes.Ldarg, i + 1);

        // Load vtable method pointer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ptrField); // load _comObject ptr onto stack

        il.Emit(OpCodes.Ldind_I); // dereference _comObject => vtbl pointer

        // Index vtable
        if (IntPtr.Size == 8)
        {
            il.Emit(OpCodes.Ldc_I8, (long)vtableIndex * 8);
            il.Emit(OpCodes.Conv_I);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4, vtableIndex * 4);
        }

        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldind_I); // final function pointer

        // Call unmanaged function pointer
        il.EmitCalli(
            OpCodes.Calli,
            CallingConvention.Cdecl,
            interfaceMethod.ReturnType,
            [typeof(void*), .. paramTypes]
        );

        il.Emit(OpCodes.Ret);

        builder.DefineMethodOverride(methodBuilder, interfaceMethod);

        return methodBuilder;
    }


    private static string GetNativeProxyName(Type type)
    {
#if DEBUG
        return $"{type.Name}NativeProxy";
#else
        return $"__DynamicImpl__{type.Name}__NativeProxy__";
#endif
    }


    private static Type CreateNativeProxyType(Type type)
    {
        string name = GetNativeProxyName(type);
        TypeBuilder builder = ModuleBuilder.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed, typeof(NativeComProxy), [type]);

        FieldInfo comPtrField = typeof(NativeComProxy).GetField("_comPtr", BindingFlags.Instance | BindingFlags.NonPublic)!;

        List<MethodInfo> methods = GetMethodTree(type);

        for (int i = 0; i < methods.Count; i++)
            BuildNativeProxyMethod(builder, methods[i], comPtrField, i);

        return builder.CreateType();
    }
}
