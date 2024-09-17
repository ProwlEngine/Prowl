// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

[FilePath("TagAndLayers.setting", FilePathAttribute.Location.Setting)]
public class TagLayerManager : ScriptableSingleton<TagLayerManager>
{
    public readonly List<string> tags =
    [
        "Untagged",
        "Main Camera",
        "Player",
        "Editor Only",
        "Re-spawn",
        "Finish",
        "Game Controller"
    ];

    public readonly string[] layers =
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

    public static string GetTag(byte index)
    {
        if (index < 0 || index >= Instance.tags.Count)
            return "Untagged";
        return Instance.tags[index];
    }

    public static string GetLayer(byte index)
    {
        if (index < 0 || index >= Instance.layers.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Layer index is out of range.");
        return Instance.layers[index];
    }

    public static byte GetTagIndex(string tag)
    {
        int index = Instance.tags.IndexOf(tag);
        return (byte)(index == -1 ? 0 : index);
    }

    public static byte GetLayerIndex(string layer)
    {
        int index = Array.IndexOf(Instance.layers, layer);
        return (byte)(index == -1 ? 0 : index);
    }

    public static void RemoveTag(byte index)
    {
        foreach (var gameObject in GameObject.FindGameObjectsWithTag(Instance.tags[index]))
            gameObject.tagIndex = 0;

        var tags = Instance.tags.ToList();
        tags.RemoveAt(index);
        foreach (var gameObject in EngineObject.FindObjectsOfType<GameObject>())
            gameObject.tagIndex = (byte)tags.IndexOf(gameObject.tag);
    }

    public static string[] GetLayers() => (string[])Instance.layers.Clone();
}
