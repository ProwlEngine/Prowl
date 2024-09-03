// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("ModelIcon.png", typeof(Mesh), ".mesh")]
    public class MeshImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            // Load the Texture into a TextureData Object and serialize to Asset Folder
            Mesh? mesh;
            try
            {
                string json = File.ReadAllText(assetPath.FullName);
                var tag = StringTagConverter.Read(json);
                mesh = Serializer.Deserialize<Mesh>(tag);
            }
            catch
            {
                Debug.LogError("Failed to deserialize mesh.");
                return;
            }

            ctx.SetMainObject(mesh);
        }
    }

}
