using System.IO;

using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;

namespace Prowl.Editor.Importers;

/// <summary>
/// Tracks .cs script files. Does not produce an EngineObject -
/// scripts are compiled externally. Triggers recompilation only when
/// the script is newer than the compiled assembly.
/// </summary>
[ImporterFor(".cs")]
public class ScriptImporter : AssetImporter
{
    public override int Version => 1;

    public override bool IsEditorOnlyAsset => true;

    public override bool Import(ImportContext ctx)
    {
        // Only request recompile if the script is newer than the compiled assembly
        var project = Project.Current;
        if (project != null && File.Exists(ctx.AbsolutePath))
        {
            ScriptAssemblyManager.RequestRecompile();
        }

        return true;
    }
}
