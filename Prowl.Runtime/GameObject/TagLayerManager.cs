// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

/// <summary>
/// Manages tags and layers for GameObjects in the Prowl Game Engine.
/// This class is a ScriptableSingleton, ensuring only one instance exists.
/// The tags and layers data is stored in a file named "TagAndLayers.setting".
/// </summary>
[FilePath("TagAndLayers.setting", FilePathAttribute.Location.Setting)]
public class TagLayerManager : ScriptableSingleton<TagLayerManager>
{
    /// <summary>
    /// List of available tags for GameObjects.
    /// </summary>
    public List<string> tags =
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
    public string[] layers =
        [
            "Default",
            "TransparentFX",
            "Ignore Raycast",
            "Water",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
        ];

    /// <summary>
    /// Retrieves the tag name associated with the given index.
    /// </summary>
    /// <param name="index">The index of the tag to retrieve.</param>
    /// <returns>The tag name at the specified index, or "Untagged" if the index is out of range.</returns>
    public static string GetTag(byte index)
    {
        if (index < 0 || index >= Instance.tags.Count)
            return "Untagged";
        return Instance.tags[index];
    }

    /// <summary>
    /// Retrieves the layer name associated with the given index.
    /// </summary>
    /// <param name="index">The index of the layer to retrieve.</param>
    /// <returns>The layer name at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public static string GetLayer(byte index)
    {
        if (index < 0 || index >= Instance.layers.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Layer index is out of range.");
        return Instance.layers[index];
    }

    /// <summary>
    /// Retrieves the index of the specified tag.
    /// </summary>
    /// <param name="tag">The tag name to look up.</param>
    /// <returns>The index of the tag, or 0 if the tag is not found.</returns>
    public static byte GetTagIndex(string tag)
    {
        int index = Instance.tags.IndexOf(tag);
        return (byte)(index == -1 ? 0 : index);
    }

    /// <summary>
    /// Retrieves the index of the specified layer.
    /// </summary>
    /// <param name="layer">The layer name to look up.</param>
    /// <returns>The index of the layer, or 0 if the layer is not found.</returns>
    public static byte GetLayerIndex(string layer)
    {
        int index = Array.IndexOf(Instance.layers, layer);
        return (byte)(index == -1 ? 0 : index);
    }

    /// <summary>
    /// Removes a tag from the list of available tags and updates all GameObjects using that tag.
    /// </summary>
    /// <param name="index">The index of the tag to remove.</param>
    public static void RemoveTag(byte index)
    {
        foreach (var gameObject in GameObject.FindGameObjectsWithTag(Instance.tags[index]))
            gameObject.tagIndex = 0;

        var tags = Instance.tags.ToList();
        tags.RemoveAt(index);
        foreach (var gameObject in EngineObject.FindObjectsOfType<GameObject>())
            gameObject.tagIndex = (byte)tags.IndexOf(gameObject.tag);
    }

    /// <summary>
    /// Retrieves a copy of the layers array.
    /// </summary>
    /// <returns>A new array containing all layer names.</returns>
    public static string[] GetLayers() => (string[])Instance.layers.Clone();
}
