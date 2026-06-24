// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Compatibility shim for the old <c>PropertyState</c> global setters. The per-object/per-material
/// <c>PropertyState</c> instance type was replaced by Graphite's <see cref="Prowl.Graphite.PropertySet"/>;
/// only the static "global uniform" entry points survive here, forwarding to <see cref="GlobalPropertySet"/>.
/// </summary>
public static class PropertyState
{
    public static void SetGlobalColor(string name, Color value) => GlobalPropertySet.SetFloat4(name, value);
    public static void SetGlobalVector(string name, Float2 value) => GlobalPropertySet.SetFloat2(name, value);
    public static void SetGlobalVector(string name, Float3 value) => GlobalPropertySet.SetFloat3(name, value);
    public static void SetGlobalVector(string name, Float4 value) => GlobalPropertySet.SetFloat4(name, value);
    public static void SetGlobalFloat(string name, float value) => GlobalPropertySet.SetFloat(name, value);
    public static void SetGlobalInt(string name, int value) => GlobalPropertySet.SetInt(name, value);
    public static void SetGlobalMatrix(string name, Float4x4 value) => GlobalPropertySet.SetMatrix(name, value);

    public static void SetGlobalTexture(string name, Texture2D? texture)
    {
        if (texture?.Handle != null)
            GlobalPropertySet.SetTexture(name, texture.Handle, texture.Sampler);
    }

    /// <summary>No-op: scoped global clearing is part of the Stage-2 render-graph rework.</summary>
    public static void ClearGlobals() { }
}
