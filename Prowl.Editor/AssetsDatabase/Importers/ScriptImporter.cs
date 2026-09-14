using System.IO;

using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;

namespace Prowl.Editor.Importers;

/// <summary> Tracks .cs script files. Does not produce an EngineObject - scripts are compiled externally. Triggers recompilation only when the script is newer than the compiled assembly. </summary>
[ImporterFor(".cs")]
public class ScriptImporter : AssetImporter
{
    public override int Version => 1;

    public override bool IsEditorOnlyAsset => true;

    /// <summary> Requests a script recompile when the source file exists. Does not produce an EngineObject. </summary>
    public override bool Import(ImportContext ctx)
    {
        // Request recompile if the project is loaded and the source file exists on disk.
        var project = Project.Current;
        if (project != null && File.Exists(ctx.AbsolutePath))
        {
            ScriptAssemblyManager.RequestRecompile();
        }

        return true;
    }
}
