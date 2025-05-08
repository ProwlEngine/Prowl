using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;

public static class ProxyEmitter
{
    private static Dictionary<Type, Type> s_proxyCache = [];

    private static AssemblyBuilder? _assemblyBuilder;
    private static ModuleBuilder? _moduleBuilder;


    private static void ValidateBuilders()
    {
        AssemblyName aName = new("COMVtableProxy");

        if (_assemblyBuilder == null)
        {
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);
        }

        if (_moduleBuilder == null)
            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(aName.Name ?? "COMVtableProxy");
    }


    private static ModuleBuilder ModuleBuilder
    {
        get
        {
            ValidateBuilders();
            return _moduleBuilder!;
        }
    }


    private static void ValidateInterface<T>() where T : IUnknown
    {
        if (!typeof(T).IsInterface)
            throw new InvalidCastException($"{typeof(T).Name} is not an interface.");
    }


    public static unsafe T CreateVtableProxy<T>(T* nativeInterfacePtr) where T : IUnknown
    {
        ValidateInterface<T>();

        return (T)Activator.CreateInstance(GetVtableProxyType<T>(), (IntPtr)nativeInterfacePtr)!;
    }


    public static Type GetVtableProxyType<T>() where T : IUnknown
    {
        ValidateInterface<T>();

        if (!s_proxyCache.TryGetValue(typeof(T), out Type? proxyType))
            s_proxyCache[typeof(T)] = proxyType = CreateVtableProxyType<T>();

        return proxyType;
    }


    private static List<MethodInfo> GetMethodTree<T>()
    {
        var methods = new List<MethodInfo>();

        Type? type = typeof(T);

        while (type != null)
        {
            methods.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Reverse());
            type = type.GetInterfaces().FirstOrDefault();
        }

        methods.Reverse();

        return methods;
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

        ILGenerator ilg = methodBuilder.GetILGenerator();

        // Load calli args onto stack
        ilg.Emit(OpCodes.Ldarg_0);
        ilg.Emit(OpCodes.Ldfld, ptrField);

        for (int i = 0; i < paramTypes.Length; i++)
            ilg.Emit(OpCodes.Ldarg, i + 1);

        // Load vtable method pointer
        ilg.Emit(OpCodes.Ldarg_0);
        ilg.Emit(OpCodes.Ldfld, ptrField); // load _comObject ptr onto stack

        ilg.Emit(OpCodes.Ldind_I); // dereference _comObject => vtbl pointer

        // Index vtable
        if (IntPtr.Size == 8)
        {
            ilg.Emit(OpCodes.Ldc_I8, (long)vtableIndex * 8);
            ilg.Emit(OpCodes.Conv_I);
        }
        else
        {
            ilg.Emit(OpCodes.Ldc_I4, vtableIndex * 4);
        }

        ilg.Emit(OpCodes.Add);
        ilg.Emit(OpCodes.Ldind_I); // final function pointer

        // Use 'calli' for unmanaged function pointer call
        ilg.EmitCalli(
            OpCodes.Calli,
            CallingConvention.Cdecl,
            interfaceMethod.ReturnType,
            [typeof(void*), .. paramTypes]
        );

        ilg.Emit(OpCodes.Ret);

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


    private static Type CreateVtableProxyType<T>()
    {
        TypeBuilder builder = ModuleBuilder.DefineType(GetProxyName<T>(), TypeAttributes.Public, typeof(ComProxy), [typeof(T)]);

        FieldInfo comPtrField = typeof(ComProxy).GetField("_comPtr", BindingFlags.Instance | BindingFlags.NonPublic)!;

        List<MethodInfo> methods = GetMethodTree<T>();

        for (int i = 0; i < methods.Count; i++)
        {
            BuildMethod(builder, methods[i], comPtrField, i);
        }

        ConstructorBuilder ctor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(IntPtr)]);
        ILGenerator ctorIL = ctor.GetILGenerator();

        ConstructorInfo baseCtor = typeof(ComProxy).GetConstructor([typeof(IntPtr)])!;
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);        // IntPtr argument
        ctorIL.Emit(OpCodes.Call, baseCtor); // call base(IntPtr)
        ctorIL.Emit(OpCodes.Ret);

        return builder.CreateType();
    }
}


public abstract class ComProxy : IUnknown
{
    protected IntPtr _comPtr;


    public ComProxy(IntPtr nativePtr)
    {
        _comPtr = nativePtr;
        AddRef();
    }


    public abstract uint AddRef();
    public abstract SlangResult QueryInterface(ref Guid uuid, out nint obj);
    public abstract uint Release();


    ~ComProxy()
    {
        Release();
    }
}