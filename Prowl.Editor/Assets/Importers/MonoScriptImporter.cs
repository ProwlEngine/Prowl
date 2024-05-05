using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("CSharpIcon.png", typeof(MonoScript), ".cs")]
    public class MonoScriptImporter : ScriptedImporter
    {
        static DateTime lastReload;

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            ctx.SetMainObject(new MonoScript());

            if (lastReload == default)
                lastReload = DateTime.UtcNow;
            else if (lastReload.AddSeconds(2) > DateTime.UtcNow)
                return;

            Program.RegisterReloadOfExternalAssemblies();

            lastReload = DateTime.UtcNow;
        }
    }

}
