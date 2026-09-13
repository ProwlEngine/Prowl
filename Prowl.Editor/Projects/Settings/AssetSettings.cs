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

    /// <summary> Applies the current asset settings to the asset loading configuration. </summary>
    public override void Apply()
    {
        AssetLoadingConfig.AsyncEnabled = AsyncAssetLoading;
    }

    /// <summary> Resets all asset settings to their default values. </summary>
    public override void ResetToDefaults()
    {
        AsyncAssetLoading = true;
    }

    /// <summary> Draws the asset settings UI in the project settings panel. </summary>
    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "assets_h_load", $"{EditorIcons.Cubes}  Loading").Underline().Show();

        Origami.Checkbox(paper, "assets_async", AsyncAssetLoading,
                v => { AsyncAssetLoading = v; AssetLoadingConfig.AsyncEnabled = v; EditorRegistries.SaveSettings(); })
            .LabelRight("Async Asset Loading").Show();
    }
}
