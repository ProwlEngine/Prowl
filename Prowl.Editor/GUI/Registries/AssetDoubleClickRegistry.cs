// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Register a handler that runs when the user double-clicks an asset with a matching
/// extension in the Project panel. Decorate a static method
/// <c>bool Handler(string relativePath, Guid guid)</c> return true if the asset was
/// handled, false to fall through to the default behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AssetDoubleClickHandlerAttribute : Attribute
{
    public string[] Extensions { get; }
    public AssetDoubleClickHandlerAttribute(params string[] extensions) => Extensions = extensions;
}

/// <summary>
/// Dispatches project-panel double-clicks to per-extension handlers. Registered via
/// <see cref="AssetDoubleClickHandlerAttribute"/> on a static method, or directly via
/// <see cref="Register"/>.
/// </summary>
public static class AssetDoubleClickRegistry
{
    public delegate bool Handler(string relativePath, Guid guid);

    private static readonly Dictionary<string, Handler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>Drop cached handler delegates (which may bind user code) so the script AssemblyLoadContext can be collected.</summary>
    [Runtime.OnAssemblyUnload]
    public static void ClearCache()
    {
        _initialized = false;
        _handlers.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _handlers.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    foreach (var attr in method.GetCustomAttributes<AssetDoubleClickHandlerAttribute>())
                    {
                        if (method.ReturnType != typeof(bool)) continue;
                        var pars = method.GetParameters();
                        if (pars.Length != 2 || pars[0].ParameterType != typeof(string) || pars[1].ParameterType != typeof(Guid)) continue;

                        Handler del;
                        try { del = (Handler)Delegate.CreateDelegate(typeof(Handler), method); }
                        catch (Exception ex) { Debug.LogWarning($"AssetDoubleClickRegistry: failed to bind {type.Name}.{method.Name}: {ex.Message}"); continue; }

                        foreach (var ext in attr.Extensions) Register(ext, del);
                    }
                }
            }
        }
    }

    /// <summary>Register a handler for a file extension (leading dot, case-insensitive).</summary>
    public static void Register(string extension, Handler handler)
    {
        if (string.IsNullOrEmpty(extension) || handler == null) return;
        _handlers[extension] = handler;
    }

    /// <summary>Invoke the registered handler, if any. Returns false if none matched or the handler returned false.</summary>
    public static bool Dispatch(string relativePath, Guid guid)
    {
        string ext = Path.GetExtension(relativePath ?? "").ToLowerInvariant();
        if (_handlers.TryGetValue(ext, out var handler))
        {
            try { return handler(relativePath!, guid); }
            catch (Exception ex) { Debug.LogError($"AssetDoubleClickRegistry: handler for {ext} threw: {ex.Message}"); }
        }
        return false;
    }
}
