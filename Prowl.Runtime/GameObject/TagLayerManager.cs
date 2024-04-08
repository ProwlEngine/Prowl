using Prowl.Runtime.Utils;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime;

[FilePath("TagAndLayers.setting", FilePathAttribute.Location.Setting)]
public class TagLayerManager : ScriptableSingleton<TagLayerManager>
{
    public List<string> tags =
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

    public List<string> layers =
        new List<string>
        {
            "Default",
            "TransparentFX",
            "Ignore Raycast",
            "Water",
        };

    public static List<string> Tags => Instance.tags;
    public static List<string> Layers => Instance.layers;

    public static string GetTag(int index) { return Instance.tags[index]; }
    public static string GetLayer(int index) { return Instance.layers[index]; }

    public static int GetTagIndex(string tag) { return Instance.tags.IndexOf(tag); }
    public static int GetLayerIndex(string layer) { return Instance.layers.IndexOf(layer); }

    public static void RemoveTag(int index)
    {
        foreach (var gameObject in GameObject.FindGameObjectsWithTag(Instance.tags[index]))
            gameObject.tagIndex = 0;

        var tags = Instance.tags.ToList();
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

        var layers = Instance.layers.ToList();
        layers.RemoveAt(index);
        foreach (var gameObject in EngineObject.FindObjectsOfType<GameObject>())
            gameObject.layerIndex = layers.IndexOf(gameObject.layer);
    }
}