using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.ParticleSystem;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
namespace Prowl.Editor.Inspector;

[CustomEditor(typeof(ParticleSystemComponent))]
public class ParticleSystemComponentEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var ps = (ParticleSystemComponent)target;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Pre-snapshot: captures entire component state before any widget mutates it
        Undo.Snapshot(ps);

        DrawPlaybackControls(paper, $"{id}_play", ps, font);
        paper.Box($"{id}_sp0").Height(4);

        // Main settings are the component's own fields, the modules are hidden from this grid.
        DrawDefaultInspector(paper, $"{id}_main", ps);
        paper.Box($"{id}_sp1").Height(6);

        Origami.Foldout(paper, $"{id}_init", $"{EditorIcons.Star}  Initial").Body(() => DrawModule(paper, $"{id}_init", ps, ps.Initial));

        Module(paper, id, "emit", EditorIcons.Burst, "Emission", ps, ps.Emission);
        Module(paper, id, "shape", EditorIcons.Shapes, "Shape", ps, ps.Shape);
        Module(paper, id, "vol", EditorIcons.Gauge, "Velocity over Lifetime", ps, ps.VelocityOverLifetime);
        Module(paper, id, "lvol", EditorIcons.GaugeSimple, "Limit Velocity over Lifetime", ps, ps.LimitVelocityOverLifetime);
        Module(paper, id, "inh", EditorIcons.ArrowsTurnRight, "Inherit Velocity", ps, ps.InheritVelocity);
        Module(paper, id, "fol", EditorIcons.Magnet, "Force over Lifetime", ps, ps.ForceOverLifetime);
        Module(paper, id, "wind", EditorIcons.Fan, "Wind", ps, ps.Wind);
        Module(paper, id, "col", EditorIcons.Palette, "Color over Lifetime", ps, ps.ColorOverLifetime);
        Module(paper, id, "cbs", EditorIcons.Palette, "Color by Speed", ps, ps.ColorBySpeed);
        Module(paper, id, "sol", EditorIcons.ArrowsLeftRight, "Size over Lifetime", ps, ps.SizeOverLifetime);
        Module(paper, id, "sbs", EditorIcons.Expand, "Size by Speed", ps, ps.SizeBySpeed);
        Module(paper, id, "rol", EditorIcons.ArrowsSpin, "Rotation over Lifetime", ps, ps.RotationOverLifetime);
        Module(paper, id, "rbs", EditorIcons.ArrowsRotate, "Rotation by Speed", ps, ps.RotationBySpeed);
        Module(paper, id, "coll", EditorIcons.Explosion, "Collision", ps, ps.Collision);
        Module(paper, id, "sub", EditorIcons.Sitemap, "Sub Emitters", ps, ps.SubEmitters);
        Module(paper, id, "sheet", EditorIcons.Film, "Texture Sheet Animation", ps, ps.TextureSheet);
        Module(paper, id, "trail", EditorIcons.Meteor, "Trails", ps, ps.Trails);
        Module(paper, id, "cdata", EditorIcons.Database, "Custom Data", ps, ps.CustomData);
        Module(paper, id, "rend", EditorIcons.Eye, "Renderer", ps, ps.Renderer);
    }

    private static void Module(Paper paper, string id, string key, string icon, string label, ParticleSystemComponent ps, ParticleSystemModule module)
        => EditorGUI.ModuleSection(paper, $"{id}_{key}", icon, label, module.Enabled, v =>
        {
            module.Enabled = v;
            EditorSceneManager.MarkDirty();
        }, () => DrawModule(paper, $"{id}_{key}", ps, module));

    private static void DrawModule(Paper paper, string id, ParticleSystemComponent ps, ParticleSystemModule module)
        => PropertyGridUtils.Draw(paper, $"{id}_grid", module, _ =>
        {
            ps.OnValidate();
            EditorSceneManager.MarkDirty();
        }, 1);

    private static void DrawPlaybackControls(Paper paper, string id, ParticleSystemComponent ps, Prowl.Scribe.FontFile font)
    {
        float fs = EditorTheme.FontSize;

        using (paper.Row(id).Height(EditorTheme.RowHeight + 4).Gap(4).PaddingLeft(4).Enter())
        {
            if (ps.IsPlaying)
                Origami.Button(paper, $"{id}_pause", $"{EditorIcons.Pause}  Pause", () => ps.Pause()).Width(70).Show();
            else
                Origami.Button(paper, $"{id}_play", $"{EditorIcons.Play}  Play", () => ps.Play()).Width(65).Show();

            Origami.Button(paper, $"{id}_restart", $"{EditorIcons.RotateRight}  Restart", () => ps.Restart()).Width(80).Show();

            Origami.Button(paper, $"{id}_stop", $"{EditorIcons.Stop}  Stop", () => ps.Stop(true, ParticleStopBehavior.StopEmittingAndClear)).Width(65).Show();

            paper.Box($"{id}_spacer").Width(UnitValue.Stretch());

            paper.Box($"{id}_count")
                .Width(UnitValue.Auto).Height(EditorTheme.RowHeight)
                .Text($"{ps.PlaybackTime:0.00}s   {ps.ParticleCount} particles", font).TextColor(EditorTheme.Ink400)
                .FontSize(fs - 2).Alignment(TextAlignment.MiddleRight);
        }
    }
}
