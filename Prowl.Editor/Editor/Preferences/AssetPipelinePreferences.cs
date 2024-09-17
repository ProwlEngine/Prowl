// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Preferences;

[FilePath("AssetPipeline.pref", FilePathAttribute.Location.EditorPreference)]
public class AssetPipelinePreferences : ScriptableSingleton<AssetPipelinePreferences>
{
    [Text("Asset Browser:")]
    public readonly bool HideExtensions = true;
    public readonly float ThumbnailSize = 0.0f;
    [Text("Pipeline:")]
    public bool AutoImport = true;
}
