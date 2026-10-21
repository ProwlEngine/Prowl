using DebuggerViewer.Rendering;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace DebuggerViewer.Scene;

internal sealed class ScenePreviewPanel : DockPanel
{
    private readonly GraphiteRenderer _renderer;
    private readonly PbrScenes _scene;

    public ScenePreviewPanel(GraphiteRenderer renderer, PbrScenes scene)
    {
        _renderer = renderer;
        _scene = scene;
    }

    public override string Title => "Scene / " + PbrScenes.Names[_scene.Selected];

    public override string Icon => "\uf030";

    public override void OnGUI(Paper paper, float width, float height)
    {
        object? image = _renderer.Preview(_scene.Target.ColorTextures[0]);
        const float bar = 36f;
        int count = PbrScenes.Names.Length;
        using (paper.Row("scene_controls").Width(width).Height(bar).Gap(6).AlignItems(LayoutAlignment.Center).Margin(8, 0, 0, 0).Enter())
        {
            Origami.IconButton(paper, "scene_prev", OrigamiGlyphs.ChevronLeft_I, () => _scene.Select((_scene.Selected + count - 1) % count)).Small().Show();
            Origami.IconButton(paper, "scene_next", OrigamiGlyphs.ChevronRight_I, () => _scene.Select((_scene.Selected + 1) % count)).Small().Show();
            if (_scene.Helmet is { } helmet)
                Origami.Switch(paper, "scene_spot", helmet.SpotOn, on => helmet.SpotOn = on).Label("Spot light").Show();
        }
        using (paper.Box("scene_preview").Width(width).Height(height - bar).IsNotInteractable().Enter())
        {
            paper.Draw((canvas, r) =>
            {
                if (image == null)
                    return;
                float w = (float)r.Size.X, h = (float)r.Size.Y;
                float aspect = (float)PbrScenes.Width / PbrScenes.Height;
                float drawW = Math.Min(w, h * aspect), drawH = drawW / aspect;
                canvas.DrawImage(image, (float)r.Min.X + (w - drawW) * 0.5f, (float)r.Min.Y + (h - drawH) * 0.5f, drawW, drawH);
            });
        }
    }
}
