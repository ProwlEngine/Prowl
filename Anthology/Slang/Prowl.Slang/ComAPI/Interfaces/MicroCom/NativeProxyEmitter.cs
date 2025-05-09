using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;

public static partial class ProxyEmitter
{
    private static Dictionary<Type, Type> s_nativeProxyCache = [];


    public static unsafe T CreateNativeProxy<T>(T* nativeInterfacePtr) where T : IUnknown
    {
        ValidateInterface<T>();

        return (T)Activator.CreateInstance(GetNativeProxyType<T>(), (IntPtr)nativeInterfacePtr)!;
    }


    public static Type GetNativeProxyType<T>() where T : IUnknown
    {
        ValidateInterface<T>();

        if (!s_nativeProxyCache.TryGetValue(typeof(T), out Type? proxyType))
            s_nativeProxyCache[typeof(T)] = proxyType = CreateNativeProxyType<T>();

        return proxyType;
    }


    private static MethodBuilder BuildMethod(TypeBuilder builder, MethodInfo interfaceMethod, FieldInfo ptrField, int vtableIndex)
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


    private static string GetProxyName<T>()
    {
#if DEBUG
        return $"{typeof(T).Name}Proxy";
#else
        return $"__DynamicImpl__{typeof(T).Name}__Proxy__";
#endif
    }


    private static Type CreateNativeProxyType<T>()
    {
        TypeBuilder builder = ModuleBuilder.DefineType(GetProxyName<T>(), TypeAttributes.Public, typeof(NativeComProxy), [typeof(T)]);

        FieldInfo comPtrField = typeof(NativeComProxy).GetField("_comPtr", BindingFlags.Instance | BindingFlags.NonPublic)!;

        List<MethodInfo> methods = GetMethodTree<T>();

        for (int i = 0; i < methods.Count; i++)
        {
            BuildMethod(builder, methods[i], comPtrField, i);
        }

        ConstructorBuilder ctor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(IntPtr)]);
        ILGenerator ctorIL = ctor.GetILGenerator();

        ConstructorInfo baseCtor = typeof(NativeComProxy).GetConstructor([typeof(IntPtr)])!;
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);        // IntPtr argument
        ctorIL.Emit(OpCodes.Call, baseCtor); // call base(IntPtr)
        ctorIL.Emit(OpCodes.Ret);

        return builder.CreateType();
    }
}
