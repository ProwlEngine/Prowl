// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Prowl.Slang.Native;

internal static partial class ProxyEmitter
{
    public const string COMAssemblyName = "Slang_COMProxy";

    private static AssemblyBuilder? _assemblyBuilder;
    private static ModuleBuilder? _moduleBuilder;


    private static void ValidateBuilders()
    {
        AssemblyName aName = new(COMAssemblyName);

        if (_assemblyBuilder == null)
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(aName, AssemblyBuilderAccess.Run);

        if (_moduleBuilder == null)
            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(aName.Name ?? COMAssemblyName);
    }


    private static ModuleBuilder ModuleBuilder
    {
        get
        {
            ValidateBuilders();
            return _moduleBuilder!;
        }
    }


    private static List<MethodInfo> GetMethodTree(Type? type)
    {
        var methods = new List<MethodInfo>();

        while (type != null)
        {
            methods.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Reverse());
            type = type.GetInterfaces().FirstOrDefault();
        }

        methods.Reverse();

        return methods;
    }


    private static void ValidateInterface<T>() where T : IUnknown
    {
        if (!typeof(T).IsInterface)
            throw new InvalidCastException($"{typeof(T).Name} is not an interface.");
    }
}
