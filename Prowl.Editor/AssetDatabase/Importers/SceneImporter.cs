using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .scene files — Echo-serialized Scene objects (native Prowl format).
/// </summary>
[ImporterFor(".scene")]
public class SceneImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        var result = new ImportResult();
        try
        {
            string text = File.ReadAllText(absolutePath);
            var echo = EchoObject.ReadFromString(text);

            var ctx = ImportHelper.CreateTrackingContext(out var dependencies);

            var scene = Serializer.Deserialize<Scene>(echo, ctx);
            if (scene != null)
            {
                scene.Name = Path.GetFileNameWithoutExtension(absolutePath);
                result.MainAsset = scene;
                result.Dependencies = dependencies;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import scene: {absolutePath}\n{ex.Message}");
        }
        return result;
    }
}
