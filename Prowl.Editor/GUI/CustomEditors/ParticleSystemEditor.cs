using System;
using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using VColor = Prowl.Vector.Color;
using Gradient = Prowl.Runtime.Gradient;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Editor.GUI;
namespace Prowl.Editor.Inspector;

// ================================================================
//  Particle System Component Editor
// ================================================================

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

        // Playback controls
        DrawPlaybackControls(paper, $"{id}_play", ps, font);

        paper.Box($"{id}_sp0").Height(4);

        // Main properties header
        paper.Box($"{id}_main_h")
            .Height(EditorTheme.RowHeight)
            .ChildLeft(8)
            .BackgroundColor(EditorTheme.Neutral300)
            .Rounded(2)
            .Margin(UnitValue.Auto, EditorTheme.Spacing)
            .Text("Particle System", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleLeft);

        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Runtime.Resources.Material>), ps.Material,
            v => ps.Material = (AssetRef<Runtime.Resources.Material>)v!, 0);

        IntRow(paper, $"{id}_maxp", "Max Particles", ps.MaxParticles, v => ps.MaxParticles = Math.Max(1, v));
        FloatRow(paper, $"{id}_dur", "Duration", ps.Duration, v => ps.Duration = MathF.Max(0.1f, v));
        BoolRow(paper, $"{id}_loop", "Looping", ps.Looping, v => ps.Looping = v);
        BoolRow(paper, $"{id}_poe", "Play On Enable", ps.PlayOnEnable, v => ps.PlayOnEnable = v);
        BoolRow(paper, $"{id}_pw", "Prewarm", ps.Prewarm, v => ps.Prewarm = v);
        EnumRow(paper, $"{id}_sim", "Simulation Space", ps.SimulationSpace, v => ps.SimulationSpace = v);

        paper.Box($"{id}_sp1").Height(6);

        // Initial Module
        DrawModule(paper, $"{id}_init", "Initial", EditorIcons.Star, ps.Initial, font, () =>
        {
            PropertyGridUtils.DrawField(paper, $"{id}_init_lt", "Start Lifetime", typeof(MinMaxCurve), ps.Initial.StartLifetime,
                v => ps.Initial.StartLifetime = v as MinMaxCurve ?? new MinMaxCurve(5f), 0);
            PropertyGridUtils.DrawField(paper, $"{id}_init_sp", "Start Speed", typeof(MinMaxCurve), ps.Initial.StartSpeed,
                v => ps.Initial.StartSpeed = v as MinMaxCurve ?? new MinMaxCurve(5f), 0);
            PropertyGridUtils.DrawField(paper, $"{id}_init_sz", "Start Size", typeof(MinMaxCurve), ps.Initial.StartSize,
                v => ps.Initial.StartSize = v as MinMaxCurve ?? new MinMaxCurve(1f), 0);
            PropertyGridUtils.DrawField(paper, $"{id}_init_rot", "Start Rotation", typeof(MinMaxCurve), ps.Initial.StartRotation,
                v => ps.Initial.StartRotation = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
            PropertyGridUtils.DrawField(paper, $"{id}_init_col", "Start Color", typeof(MinMaxGradient), ps.Initial.StartColor,
                v => ps.Initial.StartColor = v as MinMaxGradient ?? new MinMaxGradient(VColor.White), 0);
            FloatRow(paper, $"{id}_init_grav", "Gravity", ps.Initial.GravityModifier, v => ps.Initial.GravityModifier = v);
        });

        // Emission Module
        DrawModule(paper, $"{id}_emit", "Emission", EditorIcons.Burst, ps.Emission, font, () =>
        {
            PropertyGridUtils.DrawField(paper, $"{id}_emit_rot", "Rate Over Time", typeof(MinMaxCurve), ps.Emission.RateOverTime,
                v => ps.Emission.RateOverTime = v as MinMaxCurve ?? new MinMaxCurve(10f), 0);

            paper.Box($"{id}_emit_sp").Height(4);

            // Shape
            EnumRow(paper, $"{id}_emit_shape", "Shape", ps.Emission.Shape, v => ps.Emission.Shape = v);

            InspectorRow.Draw(paper, $"{id}_emit_pos", "Position", () =>
                Origami.Float3Field(paper, $"{id}_emit_pos_vf", ps.Emission.ShapePosition, v => ps.Emission.ShapePosition = v).Show());
            InspectorRow.Draw(paper, $"{id}_emit_rot2", "Rotation", () =>
                Origami.Float3Field(paper, $"{id}_emit_rot2_vf", ps.Emission.ShapeRotation, v => ps.Emission.ShapeRotation = v).Show());
            InspectorRow.Draw(paper, $"{id}_emit_scl", "Scale", () =>
                Origami.Float3Field(paper, $"{id}_emit_scl_vf", ps.Emission.ShapeScale, v => ps.Emission.ShapeScale = v).Show());

            // Shape-specific fields
            switch (ps.Emission.Shape)
            {
                case EmissionShape.LineSegment:
                    FloatRow(paper, $"{id}_emit_ll", "Line Length", ps.Emission.LineLength,
                        v => ps.Emission.LineLength = MathF.Max(0f, v));
                    break;
                case EmissionShape.Circle:
                case EmissionShape.Sphere:
                    FloatRow(paper, $"{id}_emit_rad", "Radius", ps.Emission.Radius,
                        v => ps.Emission.Radius = MathF.Max(0.01f, v));
                    BoolRow(paper, $"{id}_emit_shell", "Emit From Shell", ps.Emission.EmitFromShell,
                        v => ps.Emission.EmitFromShell = v);
                    break;
                case EmissionShape.Box:
                    InspectorRow.Draw(paper, $"{id}_emit_box", "Box Size", () =>
                        Origami.Float3Field(paper, $"{id}_emit_box_vf", ps.Emission.BoxSize, v => ps.Emission.BoxSize = v).Show());
                    break;
                case EmissionShape.Cone:
                    FloatRow(paper, $"{id}_emit_rad2", "Radius", ps.Emission.Radius,
                        v => ps.Emission.Radius = MathF.Max(0.01f, v));
                    FloatRow(paper, $"{id}_emit_angle", "Angle", ps.Emission.ConeAngle,
                        v => ps.Emission.ConeAngle = MathF.Max(0f, MathF.Min(90f, v)));
                    break;
            }

            paper.Box($"{id}_emit_sp2").Height(4);

            // Bursts
            DrawBurstList(paper, $"{id}_emit_bursts", ps.Emission.Bursts, font);
        });

        // Size Over Lifetime
        DrawModule(paper, $"{id}_sol", "Size over Lifetime", EditorIcons.ArrowsLeftRight, ps.SizeOverLifetime, font, () =>
        {
            PropertyGridUtils.DrawField(paper, $"{id}_sol_c", "Size", typeof(AnimationCurve), ps.SizeOverLifetime.SizeCurve,
                v => ps.SizeOverLifetime.SizeCurve = v as AnimationCurve ?? new AnimationCurve(), 0);
        });

        // Color Over Lifetime
        DrawModule(paper, $"{id}_col", "Color over Lifetime", EditorIcons.Palette, ps.ColorOverLifetime, font, () =>
        {
            PropertyGridUtils.DrawField(paper, $"{id}_col_g", "Color", typeof(Gradient), ps.ColorOverLifetime.ColorGradient,
                v => ps.ColorOverLifetime.ColorGradient = v as Gradient ?? new Gradient(), 0);
        });

        // Rotation Over Lifetime (still MinMaxCurve evaluated at spawn)
        DrawModule(paper, $"{id}_rol", "Rotation over Lifetime", EditorIcons.ArrowsSpin, ps.RotationOverLifetime, font, () =>
        {
            PropertyGridUtils.DrawField(paper, $"{id}_rol_av", "Angular Velocity", typeof(MinMaxCurve), ps.RotationOverLifetime.AngularVelocity,
                v => ps.RotationOverLifetime.AngularVelocity = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
        });

        // Velocity Over Lifetime
        DrawModule(paper, $"{id}_vol", "Velocity over Lifetime", EditorIcons.Gauge, ps.VelocityOverLifetime, font, () =>
        {
            PropertyGridUtils.DrawField(paper, $"{id}_vol_x", "Velocity X", typeof(AnimationCurve), ps.VelocityOverLifetime.VelocityX,
                v => ps.VelocityOverLifetime.VelocityX = v as AnimationCurve ?? new AnimationCurve(), 0);
            PropertyGridUtils.DrawField(paper, $"{id}_vol_y", "Velocity Y", typeof(AnimationCurve), ps.VelocityOverLifetime.VelocityY,
                v => ps.VelocityOverLifetime.VelocityY = v as AnimationCurve ?? new AnimationCurve(), 0);
            PropertyGridUtils.DrawField(paper, $"{id}_vol_z", "Velocity Z", typeof(AnimationCurve), ps.VelocityOverLifetime.VelocityZ,
                v => ps.VelocityOverLifetime.VelocityZ = v as AnimationCurve ?? new AnimationCurve(), 0);
        });

        // Collision
        DrawModule(paper, $"{id}_coll", "Collision", EditorIcons.Explosion, ps.Collision, font, () =>
        {
            EnumRow(paper, $"{id}_coll_mode", "Mode", ps.Collision.Mode, v => ps.Collision.Mode = v);
            EnumRow(paper, $"{id}_coll_qual", "Quality", ps.Collision.Quality, v => ps.Collision.Quality = v);
            FloatRow(paper, $"{id}_coll_damp", "Dampen", ps.Collision.Dampen,
                v => ps.Collision.Dampen = MathF.Max(0f, MathF.Min(1f, v)));
            FloatRow(paper, $"{id}_coll_bounce", "Bounce", ps.Collision.Bounce,
                v => ps.Collision.Bounce = MathF.Max(0f, MathF.Min(1f, v)));
            FloatRow(paper, $"{id}_coll_ll", "Lifetime Loss", ps.Collision.LifetimeLoss,
                v => ps.Collision.LifetimeLoss = MathF.Max(0f, v));
            FloatRow(paper, $"{id}_coll_mks", "Min Kill Speed", ps.Collision.MinKillSpeed,
                v => ps.Collision.MinKillSpeed = MathF.Max(0f, v));
            FloatRow(paper, $"{id}_coll_rad", "Particle Radius", ps.Collision.ParticleRadius,
                v => ps.Collision.ParticleRadius = MathF.Max(0.001f, v));
            FloatRow(paper, $"{id}_coll_dist", "Max Distance", ps.Collision.MaxCollisionDistance,
                v => ps.Collision.MaxCollisionDistance = MathF.Max(0.01f, v));

            PropertyGridUtils.DrawField(paper, $"{id}_coll_lm", "Collides With", typeof(LayerMask), ps.Collision.CollidesWith,
                v => ps.Collision.CollidesWith = (LayerMask)v!, 0);

            if (ps.Collision.Mode == CollisionMode.World)
            {
                FloatRow(paper, $"{id}_coll_vox", "Voxel Size", ps.Collision.VoxelSize,
                    v => ps.Collision.VoxelSize = MathF.Max(0.1f, v));
            }
        });

        // UV Animation
        DrawModule(paper, $"{id}_uv", "UV Animation", EditorIcons.Film, ps.UV, font, () =>
        {
            EnumRow(paper, $"{id}_uv_mode", "Mode", ps.UV.Mode, v => ps.UV.Mode = v);

            if (ps.UV.Mode == UVAnimationMode.GridAnimation)
            {
                IntRow(paper, $"{id}_uv_tx", "Tiles X", ps.UV.TilesX, v => ps.UV.TilesX = Math.Max(1, v));
                IntRow(paper, $"{id}_uv_ty", "Tiles Y", ps.UV.TilesY, v => ps.UV.TilesY = Math.Max(1, v));
                IntRow(paper, $"{id}_uv_cc", "Cycle Count", ps.UV.CycleCount, v => ps.UV.CycleCount = Math.Max(1, v));
                FloatRow(paper, $"{id}_uv_fot", "Frame Over Time", ps.UV.FrameOverTime,
                    v => ps.UV.FrameOverTime = MathF.Max(0f, v));
                BoolRow(paper, $"{id}_uv_rsf", "Random Start Frame", ps.UV.RandomStartFrame,
                    v => ps.UV.RandomStartFrame = v);
            }
            else
            {
                PropertyGridUtils.DrawField(paper, $"{id}_uv_uo", "U Offset", typeof(AnimationCurve), ps.UV.UOffsetCurve,
                    v => ps.UV.UOffsetCurve = v as AnimationCurve ?? new AnimationCurve(), 0);
                PropertyGridUtils.DrawField(paper, $"{id}_uv_vo", "V Offset", typeof(AnimationCurve), ps.UV.VOffsetCurve,
                    v => ps.UV.VOffsetCurve = v as AnimationCurve ?? new AnimationCurve(), 0);
                InspectorRow.Draw(paper, $"{id}_uv_ss", "Scroll Speed", () =>
                    Origami.Float2Field(paper, $"{id}_uv_ss_vf", ps.UV.ScrollSpeed, v => ps.UV.ScrollSpeed = v).Show());
            }

            BoolRow(paper, $"{id}_uv_fu", "Flip U", ps.UV.FlipU, v => ps.UV.FlipU = v);
            BoolRow(paper, $"{id}_uv_fv", "Flip V", ps.UV.FlipV, v => ps.UV.FlipV = v);
        });

        // Light: each alive particle pushes a point light into the scene's dynamic BVH.
        DrawModule(paper, $"{id}_lt", "Light", EditorIcons.Lightbulb, ps.Light, font, () =>
        {
            BoolRow(paper, $"{id}_lt_upc", "Use Particle Color", ps.Light.UseParticleColor,
                v => ps.Light.UseParticleColor = v);
            PropertyGridUtils.DrawField(paper, $"{id}_lt_col", "Tint", typeof(VColor), ps.Light.Color,
                v => ps.Light.Color = v is VColor c ? c : VColor.White, 0);
            FloatRow(paper, $"{id}_lt_int", "Intensity", ps.Light.Intensity,
                v => ps.Light.Intensity = MathF.Max(0f, v));
            FloatRow(paper, $"{id}_lt_rng", "Range", ps.Light.Range,
                v => ps.Light.Range = MathF.Max(0.01f, v));
            BoolRow(paper, $"{id}_lt_srs", "Scale Range By Size", ps.Light.ScaleRangeByParticleSize,
                v => ps.Light.ScaleRangeByParticleSize = v);
            BoolRow(paper, $"{id}_lt_fwl", "Fade With Lifetime", ps.Light.FadeWithLifetime,
                v => ps.Light.FadeWithLifetime = v);
        });
    }

    // ── Origami row helpers ────────────────────────────────────────────

    private static void FloatRow(Paper paper, string id, string label, float value, Action<float> setter)
        => InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<float>(paper, $"{id}_v", value, setter).Show());

    private static void IntRow(Paper paper, string id, string label, int value, Action<int> setter)
        => InspectorRow.Draw(paper, id, label, () =>
            Origami.NumericField<int>(paper, $"{id}_v", value, setter).Show());

    private static void EnumRow<T>(Paper paper, string id, string label, T value, Action<T> setter)
        where T : struct, Enum
        => InspectorRow.Draw(paper, id, label, () =>
            Origami.EnumDropdown<T>(paper, $"{id}_v", value, setter).Show());

    private static void BoolRow(Paper paper, string id, string label, bool value, Action<bool> setter)
        => Origami.Checkbox(paper, id, value, setter).LabelRight(label).Show();

    // ================================================================
    //  Playback Controls
    // ================================================================

    private static void DrawPlaybackControls(Paper paper, string id, ParticleSystemComponent ps, Prowl.Scribe.FontFile font)
    {
        float fs = EditorTheme.FontSize;

        using (paper.Row(id).Height(EditorTheme.RowHeight + 4).RowBetween(4).ChildLeft(4).Enter())
        {
            if (ps.IsPlaying)
            {
                Origami.Button(paper, $"{id}_pause", $"{EditorIcons.Pause}  Pause", () => { ps.Pause(); }).Width(70).Show();
                Origami.Button(paper, $"{id}_stop", $"{EditorIcons.Stop}  Stop", () => { ps.Stop(); }).Width(65).Show();
            }
            else
            {
                Origami.Button(paper, $"{id}_play", $"{EditorIcons.Play}  Play", () => { ps.Play(); }).Width(65).Show();
            }

            Origami.Button(paper, $"{id}_clear", $"{EditorIcons.Trash}  Clear", () => { ps.Clear(); }).Width(70).Show();

            paper.Box($"{id}_spacer").Width(UnitValue.Stretch());

            paper.Box($"{id}_count")
                .Width(UnitValue.Auto).Height(EditorTheme.RowHeight)
                .Text($"Particles: {ps.ParticleCount}", font).TextColor(EditorTheme.Ink400)
                .FontSize(fs - 2).Alignment(TextAlignment.MiddleRight);
        }
    }

    private static void DrawModule(Paper paper, string id, string label, string icon, ParticleSystemModule module, Prowl.Scribe.FontFile font, Action drawContents)
        => Origami.Foldout(paper, id, $"{icon}  {label}")
            .Toggle(module.Enabled, v => module.Enabled = v)
            .Body(drawContents);

    // ================================================================
    //  Burst List Editor
    // ================================================================

    private static void DrawBurstList(Paper paper, string id, List<ParticleBurst> bursts, Prowl.Scribe.FontFile font)
    {
        float fs = EditorTheme.FontSize;

        paper.Box($"{id}_lbl").Height(18)
            .Text("Bursts", font).TextColor(EditorTheme.Ink400)
            .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

        for (int i = 0; i < bursts.Count; i++)
        {
            int idx = i;
            var burst = bursts[i];

            using (paper.Column($"{id}_b{i}")
                .Height(UnitValue.Auto)
                .BackgroundColor(EditorTheme.Neutral300).Rounded(3)
                .Margin(0, 0, 0, 2)
                .Padding(6, 6, 3, 3)
                .Enter())
            {
                using (paper.Row($"{id}_bh{i}").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
                {
                    paper.Box($"{id}_bl{i}")
                        .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                        .Text($"Burst {i}", font).TextColor(EditorTheme.Ink500)
                        .FontSize(fs - 1).Alignment(TextAlignment.MiddleLeft);

                    paper.Box($"{id}_bx{i}")
                        .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(idx, (ci, _) => bursts.RemoveAt(ci));
                }

                FloatRow(paper, $"{id}_bt{i}", "Time", burst.Time,
                    v => { burst.Time = MathF.Max(0f, v); bursts[idx] = burst; });
                IntRow(paper, $"{id}_bmn{i}", "Min Count", burst.MinCount,
                    v => { burst.MinCount = Math.Max(0, v); bursts[idx] = burst; });
                IntRow(paper, $"{id}_bmx{i}", "Max Count", burst.MaxCount,
                    v => { burst.MaxCount = Math.Max(burst.MinCount, v); bursts[idx] = burst; });
                IntRow(paper, $"{id}_bcc{i}", "Cycles", burst.CycleCount,
                    v => { burst.CycleCount = Math.Max(0, v); bursts[idx] = burst; });
                FloatRow(paper, $"{id}_bri{i}", "Repeat Interval", burst.RepeatInterval,
                    v => { burst.RepeatInterval = MathF.Max(0f, v); bursts[idx] = burst; });
            }
        }

        Origami.Button(paper, $"{id}_add", $"{EditorIcons.Plus}  Add Burst", () => { bursts.Add(new ParticleBurst { Time = 0f, MinCount = 10, MaxCount = 10, CycleCount = 1, RepeatInterval = 0.01f }); }).Width(110).Show();
    }
}
