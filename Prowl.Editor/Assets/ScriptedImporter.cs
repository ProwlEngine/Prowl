using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    public class ScriptedImporter : EngineObject
    {
        public virtual void Import(SerializedAsset ctx, FileInfo assetPath) { }
    }
}
