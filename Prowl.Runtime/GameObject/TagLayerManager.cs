// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// Manages tags and layers for GameObjects in the Prowl Game Engine.
/// This class is a ScriptableSingleton, ensuring only one instance exists.
/// The tags and layers data is stored in a file named "TagAndLayers.setting".
/// </summary>
public static class TagLayerManager
{
    /// <summary>
    /// List of available tags for GameObjects.
    /// </summary>
    public static List<string> tags =
    [
        "Untagged",
        "Main Camera",
        "Player",
        "Editor Only",
        "Re-spawn",
        "Finish",
        "Game Controller"
    ];

    /// <summary>
    /// Array of available layers for GameObjects.
    /// </summary>
    public static string[] layers =
    [
        "Default",
        "TransparentFX",
        "Ignore Raycast",
        "Water",
        "UI",
        "", "", "", "",
        "", "", "", "", "",
        "", "", "", "", "",
        "", "", "", "", "",
        "", "", "", "", "",
        "", "", ""
    ];

    /// <summary>
    /// Retrieves the tag name associated with the given index.
    /// </summary>
    /// <param name="index">The index of the tag to retrieve.</param>
    /// <returns>The tag name at the specified index, or "Untagged" if the index is out of range.</returns>
    public static string GetTag(int index)
    {
        if (index < 0 || index >= tags.Count)
            return "Untagged";
        return tags[index];
    }

    /// <summary>
    /// Retrieves the layer name associated with the given index.
    /// </summary>
    /// <param name="index">The index of the layer to retrieve.</param>
    /// <returns>The layer name at the specified index, "Default" If index is out of range.</returns>
    public static string GetLayer(int index)
    {
        if (index < 0 || index >= layers.Length)
            return "Default";
        return layers[index];
    }

    /// <summary>
    /// Retrieves the index of the specified tag.
    /// </summary>
    /// <param name="tag">The tag name to look up.</param>
    /// <returns>The index of the tag, or -1 if the tag is not found.</returns>
    public static int GetTagIndex(string tag)
    {
        for (int i = 0; i < tags.Count; i++)
            if (tags[i].Equals(tag, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// Retrieves the index of the specified layer.
    /// </summary>
    /// <param name="layer">The layer name to look up.</param>
    /// <returns>The index of the layer, or -1 if the layer is not found.</returns>
    public static int GetLayerIndex(string layer)
    {
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].Equals(layer, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// Retrieves a copy of the layers array.
    /// </summary>
    /// <returns>A new array containing all layer names.</returns>
    public static IReadOnlyList<string> GetLayers() => layers;

    private static List<SortingLayerInfo> s_sortingLayers = DefaultSortingLayers();

    /// <summary>Sorting layers back to front. Default is always one of them.</summary>
    public static IReadOnlyList<SortingLayerInfo> SortingLayers => s_sortingLayers;

    /// <summary>Moves each time the sorting layers change, so parked renderers sort again.</summary>
    public static int SortingLayersVersion { get; private set; }

    /// <summary>Replaces the sorting layers, keeping their order. Default is added at the front when missing.</summary>
    public static void SetSortingLayers(IEnumerable<SortingLayerInfo> layers)
    {
        List<SortingLayerInfo> copy = [];
        foreach (SortingLayerInfo layer in layers)
            if (!copy.Exists(l => l.Id == layer.Id))
                copy.Add(new SortingLayerInfo { Id = layer.Id, Name = layer.Name });
        if (!copy.Exists(l => l.Id == SortingLayer.DefaultId))
            copy.Insert(0, new SortingLayerInfo { Id = SortingLayer.DefaultId, Name = "Default" });
        s_sortingLayers = copy;
        SortingLayersVersion++;
    }

    /// <summary>The name of a sorting layer, or Default's when the id is not one.</summary>
    public static string GetSortingLayerName(int id)
    {
        int index = SortingLayerIndex(id);
        return s_sortingLayers[index < 0 ? SortingLayerIndex(SortingLayer.DefaultId) : index].Name;
    }

    /// <summary>
    /// The transparent sort order of a sorting layer and an order in it: the layer's place relative to Default, then the
    /// order clamped to a short. Default at order 0 is 0, where every renderer without a sorting layer sorts.
    /// </summary>
    public static int SortingOrderOf(int layerId, int order)
    {
        int index = SortingLayerIndex(layerId);
        int rank = index < 0 ? 0 : index - SortingLayerIndex(SortingLayer.DefaultId);
        return rank * 65536 + Math.Clamp(order, short.MinValue, short.MaxValue);
    }

    private static int SortingLayerIndex(int id) => s_sortingLayers.FindIndex(l => l.Id == id);

    private static List<SortingLayerInfo> DefaultSortingLayers() => [new SortingLayerInfo { Id = SortingLayer.DefaultId, Name = "Default" }];

    public static void ResetDefault()
    {
        s_sortingLayers = DefaultSortingLayers();
        SortingLayersVersion++;

        tags =
        [
            "Untagged",
            "Main Camera",
            "Player",
            "Editor Only",
            "Re-spawn",
            "Finish",
            "Game Controller"
        ];

        // Array of available layers for GameObjects.
        layers =
        [
            "Default",
            "TransparentFX",
            "Ignore Raycast",
            "Water",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", "", "", "",
            "", "", ""
        ];
    }
}

/// <summary>A sorting layer as the project lists it. The id stays the same through renames and moves.</summary>
public sealed class SortingLayerInfo
{
    public int Id;
    public string Name = "";
}

/// <summary>A renderer's sorting layer, kept by id so renaming or moving the layer keeps the renderer on it.</summary>
public struct SortingLayer : IEquatable<SortingLayer>
{
    public const int DefaultId = 0;

    public int Id;

    public SortingLayer(int id) => Id = id;

    public static SortingLayer Default => new(DefaultId);

    public readonly string Name => TagLayerManager.GetSortingLayerName(Id);

    public readonly bool Equals(SortingLayer other) => Id == other.Id;
    public override readonly bool Equals(object? obj) => obj is SortingLayer other && Equals(other);
    public override readonly int GetHashCode() => Id;
    public static bool operator ==(SortingLayer a, SortingLayer b) => a.Id == b.Id;
    public static bool operator !=(SortingLayer a, SortingLayer b) => a.Id != b.Id;
    public override readonly string ToString() => Name;
}
