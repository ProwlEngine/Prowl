using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .mat files — Echo-serialized Material objects (native Prowl format).
/// </summary>
[ImporterFor(".mat")]
public class MaterialImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        var result = new ImportResult();
        try
        {
            string text = File.ReadAllText(absolutePath);
            var echo = EchoObject.ReadFromString(text);

            var ctx = new SerializationContext();
            AssetDatabase.ConfigureContext(ctx);

            var material = Serializer.Deserialize<Material>(echo, ctx);
            if (material != null)
            {
                material.Name = Path.GetFileNameWithoutExtension(absolutePath);
                result.MainAsset = material;

                // Track dependencies discovered during deserialization
                // (shader, textures referenced via $assetId)
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import material: {absolutePath}\n{ex.Message}");
        }
        return result;
    }
}
