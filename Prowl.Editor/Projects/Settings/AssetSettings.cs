using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// Asset pipeline settings. Controls whether assets stream in on a background thread
/// (default) or load synchronously on demand.
/// </summary>
[ProjectSettings("Assets", EditorIcons.Cubes, order: 30)]
public class AssetSettings : ProjectSettingsBase
{
    /// <summary>
    /// When true, <see cref="AssetRef{T}"/> resolves on a background thread: scenes appear
    /// immediately and meshes/textures pop in as they finish loading. When false, asset
    /// access blocks until the asset is fully loaded (legacy behavior).
    /// </summary>
    public bool AsyncAssetLoading = true;

    public override void Apply()
    {
        AssetLoadingConfig.AsyncEnabled = AsyncAssetLoading;
    }

    public override void ResetToDefaults()
    {
        AsyncAssetLoading = true;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "assets_h_load", $"{EditorIcons.Cubes}  Loading").Underline().Show();

        Origami.Checkbox(paper, "assets_async", AsyncAssetLoading,
                v => { AsyncAssetLoading = v; AssetLoadingConfig.AsyncEnabled = v; EditorRegistries.SaveSettings(); })
            .LabelRight("Async Asset Loading").Show();
    }
}
