using Prowl.Echo;

namespace Prowl.Editor.Importers;

/// <summary>
/// Tracks .cs script files. Does not produce an EngineObject —
/// scripts are compiled externally, not imported as assets.
/// </summary>
[ImporterFor(".cs")]
public class ScriptImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        // Scripts are tracked but not imported as runtime assets.
        // The database records them for dependency tracking and recompile detection.
        return new ImportResult();
    }
}
