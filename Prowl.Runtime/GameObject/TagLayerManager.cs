using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime;

public class TagLayerManager
{
    public static List<string> tags =
        new List<string>
        {
            "Untagged",
            "Main Camera",
            "Player",
            "Editor Only",
            "Re-spawn",
            "Finish",
            "Game Controller",
        };

    public static List<string> layers =
        new List<string>
        {
            "Default",
            "TransparentFX",
            "Ignore Raycast",
            "Water",
        };

    public static int GetTagIndex(string tag) { return TagLayerManager.tags.IndexOf(tag); }
    public static int GetLayerIndex(string layer) { return TagLayerManager.layers.IndexOf(layer); }

    public static void RemoveTag(int index)
    {
        foreach (var gameObject in GameObject.FindGameObjectsWithTag(TagLayerManager.tags[index]))
            gameObject.tagIndex = 0;

        var tags = TagLayerManager.tags.ToList();
        tags.RemoveAt(index);
        foreach (var gameObject in EngineObject.FindObjectsOfType<GameObject>())
            gameObject.tagIndex = tags.IndexOf(gameObject.tag);
    }

    public static void RemoveLayer(int index)
    {
        foreach (var gameObject in EngineObject.FindObjectsOfType<GameObject>())
        {
            if (gameObject.layerIndex == index)
                gameObject.layerIndex = 0;
        }

        var layers = TagLayerManager.layers.ToList();
        layers.RemoveAt(index);
        foreach (var gameObject in EngineObject.FindObjectsOfType<GameObject>())
            gameObject.layerIndex = layers.IndexOf(gameObject.layer);
    }
}