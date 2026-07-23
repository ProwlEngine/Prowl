// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Utils;
using Prowl.Runtime;

namespace Prowl.Editor.Core;

/// <summary>
/// Discovers and invokes the script hot-reload lifecycle hooks (<see cref="OnAssemblyUnloadAttribute"/>,
/// <see cref="OnAssemblyLoadAttribute"/>, <see cref="OnScriptCompileAttribute"/>). Any static
/// parameterless method tagged with one of those attributes (in the engine, the editor, or user
/// scripts) is run automatically at the matching phase.
/// </summary>
/// <remarks>
/// It's important that the types are never cached: this avoids pinning the ALC down before a reload and
/// allows for any script to subscribe at any point during the lifetime of the application.
/// </remarks>
public static class ScriptReloadCallbacks
{
    /// <summary>Run all <see cref="OnScriptCompileAttribute"/> hooks (compilation is starting).</summary>
    public static void InvokeScriptCompile() => Invoke<OnScriptCompileAttribute>("OnScriptCompile");

    /// <summary>Run all <see cref="OnAssemblyUnloadAttribute"/> hooks (about to unload the script context).</summary>
    public static void InvokeAssemblyUnload() => Invoke<OnAssemblyUnloadAttribute>("OnAssemblyUnload");

    /// <summary>Run all <see cref="OnAssemblyLoadAttribute"/> hooks (new script assemblies are loaded).</summary>
    public static void InvokeAssemblyLoad() => Invoke<OnAssemblyLoadAttribute>("OnAssemblyLoad");

    private static void Invoke<TAttr>(string phase) where TAttr : ScriptLifecycleAttribute
    {
        var hooks = new List<(int order, MethodInfo method)>();

        foreach (Type type in EditorUtils.GetAllTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<TAttr>();
                if (attr == null) continue;

                if (method.GetParameters().Length != 0)
                {
                    Runtime.Debug.LogWarning($"[ScriptReloadCallbacks] [{phase}] {type.FullName}.{method.Name} must be static and parameterless; skipping.");
                    continue;
                }

                hooks.Add((attr.Order, method));
            }
        }

        foreach (var (_, method) in hooks.OrderBy(h => h.order))
        {
            try
            {
                method.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"[ScriptReloadCallbacks] [{phase}] {method.DeclaringType?.Name}.{method.Name} threw: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
    }
}
