using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime.Utils
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class OnAssemblyUnloadAttribute : Attribute
    {
        public OnAssemblyUnloadAttribute()
        {
        }

        private static List<MethodInfo> methodInfos = [];

        public static void Invoke()
        {
            foreach (var methodInfo in methodInfos)
            {
                methodInfo.Invoke(null, null);
            }
        }

        public static void FindAll()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var method in methods)
                    {
                        var attributes = method.GetCustomAttributes<OnAssemblyUnloadAttribute>();
                        if (attributes.Count() > 0)
                        {
                            methodInfos.Add(method);
                        }
                    }
                }
            }
        }

    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class OnAssemblyLoadAttribute(int order = 0) : Attribute
    {
        int order = order;
        private static List<MethodInfo> methodInfos = [];

        public static void Invoke()
        {
            foreach (var methodInfo in methodInfos)
            {
                methodInfo.Invoke(null, null);
            }
        }

        public static void FindAll()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<(MethodInfo, int)> attribMethods = [];
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var method in methods)
                    {
                        var attribute = method.GetCustomAttribute<OnAssemblyLoadAttribute>();
                        if (attribute != null)
                        {
                            attribMethods.Add((method, attribute.order));
                        }
                    }
                }
            }
            var ordered = attribMethods.OrderBy(x => x.Item2);
            foreach (var attribMethod in ordered)
            {
                methodInfos.Add(attribMethod.Item1);
            }
        }

    }
}
