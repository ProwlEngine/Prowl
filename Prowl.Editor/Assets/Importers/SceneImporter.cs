// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

[Importer("HierarchyIcon.png", typeof(Scene), ".scene")]
public class SceneImporter : ScriptedImporter
{
    public override void Import(SerializedAsset ctx, FileInfo assetPath)
    {
        var tag = EchoObject.ReadFromString(assetPath);
        Scene? scene = Serializer.Deserialize<Scene>(tag) ?? throw new Exception("Failed to Deserialize Scene.");
        ctx.SetMainObject(scene);
    }
}
