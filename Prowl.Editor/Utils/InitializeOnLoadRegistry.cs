// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Reflection;

using Prowl.Editor.Utils;
using Prowl.Runtime;

namespace Prowl.Editor.Core;

/// <summary>
/// Mark a static method to be invoked automatically when the editor loads
/// or when script assemblies are reloaded.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class InitializeOnLoadAttribute : Attribute { }

/// <summary>
/// Scans all loaded assemblies for static methods decorated with
/// <see cref="InitializeOnLoadAttribute"/> and invokes them.
/// </summary>
public static class InitializeOnLoadRegistry
{
    private static bool _initialized;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        int count = 0;

        foreach (var type in EditorUtils.GetAllTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.GetCustomAttribute<InitializeOnLoadAttribute>() == null) continue;
                if (method.GetParameters().Length != 0) continue;

                try
                {
                    method.Invoke(null, null);
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[InitializeOnLoad] {type.Name}.{method.Name} threw: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        Debug.Log($"InitializeOnLoadRegistry: Invoked {count} methods.");
    }
}
