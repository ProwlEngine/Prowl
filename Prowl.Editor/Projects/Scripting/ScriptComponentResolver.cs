// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Editor.GUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Maps a dragged script asset (a <c>.cs</c> file) to the component <see cref="Type"/> it defines.
/// Scripts are not runtime assets (the importer only triggers recompilation), so the type is resolved
/// by matching the file name against a <see cref="MonoBehaviour"/> type of the same name in the loaded
/// assemblies - the same file-name-equals-class-name convention the script pipeline already relies on.
/// </summary>
public static class ScriptComponentResolver
{
    /// <summary>Resolve the component type a dragged asset represents, or null if it isn't a single
    /// script file that maps to a concrete <see cref="MonoBehaviour"/>.</summary>
    public static Type? ResolveComponentType(AssetDragPayload? payload)
    {
        if (payload == null || payload.IsMulti) return null;
        return ResolveComponentType(payload.AssetName);
    }

    /// <summary>Resolve the component type for a script file name/path (must end in <c>.cs</c>).</summary>
    public static Type? ResolveComponentType(string? fileNameOrPath)
    {
        if (string.IsNullOrEmpty(fileNameOrPath)) return null;
        if (!fileNameOrPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return null;

        string typeName = Path.GetFileNameWithoutExtension(fileNameOrPath);
        if (string.IsNullOrEmpty(typeName)) return null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.Name != typeName) continue;
                if (type.IsAbstract) continue;
                if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                if (type == typeof(MonoBehaviour)) continue;
                return type;
            }
        }

        return null;
    }
}
