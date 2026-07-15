using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .inputactions files Echo-serialized InputActionMap objects.
/// </summary>
[ImporterFor(".inputactions")]
public class InputActionMapImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            string text = File.ReadAllText(ctx.AbsolutePath);
            var echo = EchoObject.ReadFromString(text);

            var map = Serializer.Deserialize<InputActionMap>(echo);
            if (map != null)
                ctx.SetMainAsset(map);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import input actions: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
