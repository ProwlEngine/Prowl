using System;
using System.Collections.Generic;

using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// Result returned by an AssetImporter after processing a raw file.
/// </summary>
public class ImportResult
{
    /// <summary>The primary imported object (null for script-only assets).</summary>
    public EngineObject? MainAsset;

    /// <summary>Additional objects produced (e.g. meshes from a model file).</summary>
    public EngineObject[]? SubAssets;

    /// <summary>Asset GUIDs that this asset references/depends on.</summary>
    public HashSet<Guid> Dependencies = new();
}
