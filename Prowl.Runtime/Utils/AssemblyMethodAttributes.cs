// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime.Utils;

public abstract class AssemblyMethodAttributeBase : Attribute
{
    protected readonly int order;

    protected AssemblyMethodAttributeBase(int order = 0)
    {
        this.order = order;
    }

    protected static class Storage<T> where T : AssemblyMethodAttributeBase
    {
        private static readonly List<(MethodInfo Method, int Order)> methodInfos = [];

        public static void Invoke()
        {
            foreach ((MethodInfo method, int _) in methodInfos)
            {
                method.Invoke(null, null);
            }
        }

        public static void Clear() => methodInfos.Clear();

        public static void FindAll()
        {
            methodInfos.Clear();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                Type[] types = assembly.GetTypes();
                foreach (Type type in types)
                {
                    MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (MethodInfo method in methods)
                    {
                        T? attribute = method.GetCustomAttribute<T>();
                        if (attribute != null)
                        {
                            methodInfos.Add((method, attribute.order));
                        }
                    }
                }
            }

            var ordered = methodInfos.OrderBy(x => x.Order).ToList();
            methodInfos.Clear();
            methodInfos.AddRange(ordered);
        }
    }

    public static void FindAll()
    {
        OnAssemblyUnloadAttribute.FindAll();
        OnAssemblyLoadAttribute.FindAll();

        OnSceneLoadAttribute.FindAll();
        OnSceneUnloadAttribute.FindAll();

        OnPlaymodeChangedAttribute.FindAll();
    }

    public static void Clear()
    {
        OnAssemblyUnloadAttribute.Clear();
        OnAssemblyLoadAttribute.Clear();

        OnSceneLoadAttribute.Clear();
        OnSceneUnloadAttribute.Clear();

        OnPlaymodeChangedAttribute.Clear();
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OnAssemblyUnloadAttribute : AssemblyMethodAttributeBase
{
    public OnAssemblyUnloadAttribute(int order = 0) : base(order) { }

    public static void Invoke() => Storage<OnAssemblyUnloadAttribute>.Invoke();
    public static void Clear() => Storage<OnAssemblyUnloadAttribute>.Clear();
    public static void FindAll() => Storage<OnAssemblyUnloadAttribute>.FindAll();
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OnAssemblyLoadAttribute : AssemblyMethodAttributeBase
{
    public OnAssemblyLoadAttribute(int order = 0) : base(order) { }

    public static void Invoke() => Storage<OnAssemblyLoadAttribute>.Invoke();
    public static void Clear() => Storage<OnAssemblyLoadAttribute>.Clear();
    public static void FindAll() => Storage<OnAssemblyLoadAttribute>.FindAll();
}

#region Scenes

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OnSceneLoadAttribute : AssemblyMethodAttributeBase
{
    public OnSceneLoadAttribute(int order = 0) : base(order) { }

    public static void Invoke() => Storage<OnSceneLoadAttribute>.Invoke();
    public static void Clear() => Storage<OnSceneLoadAttribute>.Clear();
    public static void FindAll() => Storage<OnSceneLoadAttribute>.FindAll();
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OnSceneUnloadAttribute : AssemblyMethodAttributeBase
{
    public OnSceneUnloadAttribute(int order = 0) : base(order) { }

    public static void Invoke() => Storage<OnSceneUnloadAttribute>.Invoke();
    public static void Clear() => Storage<OnSceneUnloadAttribute>.Clear();
    public static void FindAll() => Storage<OnSceneUnloadAttribute>.FindAll();
}

#endregion

#region Editor

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OnPlaymodeChangedAttribute : AssemblyMethodAttributeBase
{
    public OnPlaymodeChangedAttribute(int order = 0) : base(order) { }

    public static void Invoke() => Storage<OnPlaymodeChangedAttribute>.Invoke();
    public static void Clear() => Storage<OnPlaymodeChangedAttribute>.Clear();
    public static void FindAll() => Storage<OnPlaymodeChangedAttribute>.FindAll();
}

#endregion
