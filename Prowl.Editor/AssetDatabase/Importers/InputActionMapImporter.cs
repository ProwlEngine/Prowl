using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .inputactions files — Echo-serialized InputActionMap objects.
/// </summary>
[ImporterFor(".inputactions")]
public class InputActionMapImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        var result = new ImportResult();
        try
        {
            string text = File.ReadAllText(absolutePath);
            var echo = EchoObject.ReadFromString(text);

            var map = Serializer.Deserialize<InputActionMap>(echo);
            if (map != null)
            {
                map.Name = Path.GetFileNameWithoutExtension(absolutePath);
                result.MainAsset = map;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import input actions: {absolutePath}\n{ex.Message}");
        }
        return result;
    }
}
