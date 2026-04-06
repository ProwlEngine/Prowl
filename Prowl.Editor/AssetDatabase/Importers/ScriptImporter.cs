using System.IO;

using Prowl.Echo;
using Prowl.Editor.Scripting;

namespace Prowl.Editor.Importers;

/// <summary>
/// Tracks .cs script files. Does not produce an EngineObject —
/// scripts are compiled externally. Triggers recompilation only when
/// the script is newer than the compiled assembly.
/// </summary>
[ImporterFor(".cs")]
public class ScriptImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        // Only request recompile if the script is newer than the compiled assembly
        var project = Project.Current;
        if (project != null && File.Exists(absolutePath))
        {
            var scriptTime = File.GetLastWriteTimeUtc(absolutePath);
            bool needsRecompile = true;

            // Check if Game assembly exists and is newer
            if (File.Exists(project.GameAssemblyPath))
            {
                var dllTime = File.GetLastWriteTimeUtc(project.GameAssemblyPath);
                needsRecompile = scriptTime > dllTime;
            }

            if (needsRecompile)
                ScriptAssemblyManager.RequestRecompile();
        }

        return new ImportResult();
    }
}
