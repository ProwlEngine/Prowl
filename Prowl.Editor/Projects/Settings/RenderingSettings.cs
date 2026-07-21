using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Projects.Settings;

[ProjectSettings("Rendering", EditorIcons.Camera, order: 21)]
public class RenderingSettings : ProjectSettingsBase
{
    public AssetRef<RenderPipelineAsset> PipelineAsset;

    public override void Apply()
    {
        RenderPipelineManager.Asset = PipelineAsset;
    }

    public override void ResetToDefaults()
    {
        PipelineAsset = default;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "render_header", $"{EditorIcons.Camera}  Rendering").Underline().Show();

        PropertyGridUtils.DrawField(paper, "render_pipeline_v", "Pipeline Asset", typeof(AssetRef<RenderPipelineAsset>), PipelineAsset,
            v =>
            {
                PipelineAsset = (AssetRef<RenderPipelineAsset>)v!;
                Apply();
                EditorRegistries.SaveSettings();
            }, 0);
    }
}
