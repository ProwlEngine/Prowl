// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A component that draws with materials, one per slot. Lets code and visual scripting swap or edit materials
/// without knowing which renderer it is. Single material renderers have exactly one slot.
/// </summary>
public interface IMaterialRenderer
{
    /// <summary>How many material slots the renderer has.</summary>
    int MaterialSlotCount { get; }

    /// <summary>The material in <paramref name="slot"/>, or null when the slot uses the renderer's default.</summary>
    Material? GetMaterial(int slot);

    /// <summary>Assigns <paramref name="slot"/>. Renderers with a material list also accept the slot just past the end, which adds one.</summary>
    void SetMaterial(int slot, Material? material);
}

/// <summary>A component whose look is tinted by a single color, such as a sprite, a skinned mesh, 3D text or a UI graphic.</summary>
public interface IColorTint
{
    /// <summary>The tint color.</summary>
    Color Tint { get; set; }
}

internal static class MaterialSlots
{
    public static void CheckSingle(int slot)
    {
        if (slot != 0) throw new ArgumentOutOfRangeException(nameof(slot), slot, "This renderer has a single material slot, 0.");
    }

    public static Material? Get(System.Collections.Generic.List<Material> materials, int slot)
    {
        if ((uint)slot >= (uint)materials.Count) throw new ArgumentOutOfRangeException(nameof(slot), slot, $"The renderer has {materials.Count} material slots.");
        return materials[slot];
    }

    public static void Set(System.Collections.Generic.List<Material> materials, int slot, Material? material)
    {
        if (slot == materials.Count) materials.Add(material!);
        else if ((uint)slot < (uint)materials.Count) materials[slot] = material!;
        else throw new ArgumentOutOfRangeException(nameof(slot), slot, $"The renderer has {materials.Count} material slots.");
    }
}
