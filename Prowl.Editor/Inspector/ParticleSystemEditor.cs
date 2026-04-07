using System;
using System.Collections.Generic;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Vector;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;
using Gradient = Prowl.Runtime.ParticleSystem.Gradient;

namespace Prowl.Editor.Inspector;

// ================================================================
//  MinMaxCurve Property Editor
// ================================================================

[CustomPropertyEditor(typeof(MinMaxCurve))]
public class MinMaxCurvePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var curve = value as MinMaxCurve ?? new MinMaxCurve();

        using (paper.Column(id).Height(UnitValue.Auto).Enter())
        {
            // Mode selector + label on one row
            EditorGUI.EnumDropdown(paper, $"{id}_mode", label, curve.Mode)
                .OnValueChanged(v => { curve.Mode = v; onChange(curve); });

            switch (curve.Mode)
            {
                case MinMaxCurveMode.Constant:
                    EditorGUI.FloatField(paper, $"{id}_val", curve.ConstantValue, "Value")
                        .OnValueChanged(v => { curve.ConstantValue = v; onChange(curve); });
                    break;

                case MinMaxCurveMode.Curve:
                    CurveEditor.CurveField(paper, $"{id}_curve", "Curve", curve.Curve)
                        .OnValueChanged(v => { curve.Curve = v; onChange(curve); });
                    break;

                case MinMaxCurveMode.Random:
                    EditorGUI.FloatField(paper, $"{id}_min", curve.MinValue, "Min")
                        .OnValueChanged(v => { curve.MinValue = v; onChange(curve); });
                    EditorGUI.FloatField(paper, $"{id}_max", curve.MaxValue, "Max")
                        .OnValueChanged(v => { curve.MaxValue = v; onChange(curve); });
                    break;
            }
        }
    }
}

// ================================================================
//  MinMaxGradient Property Editor
// ================================================================

[CustomPropertyEditor(typeof(MinMaxGradient))]
public class MinMaxGradientPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var gradient = value as MinMaxGradient ?? new MinMaxGradient();

        using (paper.Column(id).Height(UnitValue.Auto).Enter())
        {
            EditorGUI.EnumDropdown(paper, $"{id}_mode", label, gradient.Mode)
                .OnValueChanged(v => { gradient.Mode = v; onChange(gradient); });

            switch (gradient.Mode)
            {
                case MinMaxGradientMode.Color:
                    EditorGUI.ColorField(paper, $"{id}_color", "Color", gradient.ConstantColor)
                        .OnValueChanged(v => { gradient.ConstantColor = v; onChange(gradient); });
                    break;

                case MinMaxGradientMode.Gradient:
                    PropertyGrid.DrawField(paper, $"{id}_grad", "Gradient", typeof(Gradient), gradient.Gradient,
                        v => { gradient.Gradient = v as Gradient ?? new Gradient(); onChange(gradient); }, 1);
                    break;

                case MinMaxGradientMode.RandomBetweenTwoColors:
                    EditorGUI.ColorField(paper, $"{id}_minc", "Min Color", gradient.MinColor)
                        .OnValueChanged(v => { gradient.MinColor = v; onChange(gradient); });
                    EditorGUI.ColorField(paper, $"{id}_maxc", "Max Color", gradient.MaxColor)
                        .OnValueChanged(v => { gradient.MaxColor = v; onChange(gradient); });
                    break;

                case MinMaxGradientMode.RandomBetweenTwoGradients:
                    PropertyGrid.DrawField(paper, $"{id}_ming", "Min Gradient", typeof(Gradient), gradient.MinGradient,
                        v => { gradient.MinGradient = v as Gradient ?? new Gradient(); onChange(gradient); }, 1);
                    PropertyGrid.DrawField(paper, $"{id}_maxg", "Max Gradient", typeof(Gradient), gradient.MaxGradient,
                        v => { gradient.MaxGradient = v as Gradient ?? new Gradient(); onChange(gradient); }, 1);
                    break;
            }
        }
    }
}

// ================================================================
//  Particle System Component Editor
// ================================================================

[CustomComponentEditor(typeof(ParticleSystemComponent))]
public class ParticleSystemComponentEditor : ComponentEditor
{
    public override void OnGUI(Paper paper, string id, MonoBehaviour component)
    {
        var ps = (ParticleSystemComponent)component;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Playback controls
        DrawPlaybackControls(paper, $"{id}_play", ps, font);

        paper.Box($"{id}_sp0").Height(4);

        // Main properties
        DrawModuleHeader(paper, $"{id}_main_h", "Particle System", null, font, true);

        PropertyGrid.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Runtime.Resources.Material>), ps.Material,
            v => ps.Material = (AssetRef<Runtime.Resources.Material>)v!, 0);

        EditorGUI.IntField(paper, $"{id}_maxp", ps.MaxParticles, "Max Particles")
            .OnValueChanged(v => ps.MaxParticles = Math.Max(1, v));
        EditorGUI.FloatField(paper, $"{id}_dur", ps.Duration, "Duration")
            .OnValueChanged(v => ps.Duration = MathF.Max(0.1f, v));
        EditorGUI.Toggle(paper, $"{id}_loop", "Looping", ps.Looping)
            .OnValueChanged(v => ps.Looping = v);
        EditorGUI.Toggle(paper, $"{id}_poe", "Play On Enable", ps.PlayOnEnable)
            .OnValueChanged(v => ps.PlayOnEnable = v);
        EditorGUI.Toggle(paper, $"{id}_pw", "Prewarm", ps.Prewarm)
            .OnValueChanged(v => ps.Prewarm = v);
        EditorGUI.EnumDropdown(paper, $"{id}_sim", "Simulation Space", ps.SimulationSpace)
            .OnValueChanged(v => ps.SimulationSpace = v);

        paper.Box($"{id}_sp1").Height(6);

        // Initial Module
        DrawModule(paper, $"{id}_init", "Initial", EditorIcons.Star, ps.Initial, font, () =>
        {
            PropertyGrid.DrawField(paper, $"{id}_init_lt", "Start Lifetime", typeof(MinMaxCurve), ps.Initial.StartLifetime,
                v => ps.Initial.StartLifetime = v as MinMaxCurve ?? new MinMaxCurve(5f), 0);
            PropertyGrid.DrawField(paper, $"{id}_init_sp", "Start Speed", typeof(MinMaxCurve), ps.Initial.StartSpeed,
                v => ps.Initial.StartSpeed = v as MinMaxCurve ?? new MinMaxCurve(5f), 0);
            PropertyGrid.DrawField(paper, $"{id}_init_sz", "Start Size", typeof(MinMaxCurve), ps.Initial.StartSize,
                v => ps.Initial.StartSize = v as MinMaxCurve ?? new MinMaxCurve(1f), 0);
            PropertyGrid.DrawField(paper, $"{id}_init_rot", "Start Rotation", typeof(MinMaxCurve), ps.Initial.StartRotation,
                v => ps.Initial.StartRotation = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
            PropertyGrid.DrawField(paper, $"{id}_init_col", "Start Color", typeof(MinMaxGradient), ps.Initial.StartColor,
                v => ps.Initial.StartColor = v as MinMaxGradient ?? new MinMaxGradient(VColor.White), 0);
            EditorGUI.FloatField(paper, $"{id}_init_grav", ps.Initial.GravityModifier, "Gravity")
                .OnValueChanged(v => ps.Initial.GravityModifier = v);
        });

        // Emission Module
        DrawModule(paper, $"{id}_emit", "Emission", EditorIcons.Burst, ps.Emission, font, () =>
        {
            PropertyGrid.DrawField(paper, $"{id}_emit_rot", "Rate Over Time", typeof(MinMaxCurve), ps.Emission.RateOverTime,
                v => ps.Emission.RateOverTime = v as MinMaxCurve ?? new MinMaxCurve(10f), 0);

            paper.Box($"{id}_emit_sp").Height(4);

            // Shape
            EditorGUI.EnumDropdown(paper, $"{id}_emit_shape", "Shape", ps.Emission.Shape)
                .OnValueChanged(v => ps.Emission.Shape = v);

            EditorGUI.Vector3Field(paper, $"{id}_emit_pos", "Position", ps.Emission.ShapePosition)
                .OnValueChanged(v => ps.Emission.ShapePosition = v);
            EditorGUI.Vector3Field(paper, $"{id}_emit_rot2", "Rotation", ps.Emission.ShapeRotation)
                .OnValueChanged(v => ps.Emission.ShapeRotation = v);
            EditorGUI.Vector3Field(paper, $"{id}_emit_scl", "Scale", ps.Emission.ShapeScale)
                .OnValueChanged(v => ps.Emission.ShapeScale = v);

            // Shape-specific fields
            switch (ps.Emission.Shape)
            {
                case EmissionShape.LineSegment:
                    EditorGUI.FloatField(paper, $"{id}_emit_ll", ps.Emission.LineLength, "Line Length")
                        .OnValueChanged(v => ps.Emission.LineLength = MathF.Max(0f, v));
                    break;
                case EmissionShape.Circle:
                case EmissionShape.Sphere:
                    EditorGUI.FloatField(paper, $"{id}_emit_rad", ps.Emission.Radius, "Radius")
                        .OnValueChanged(v => ps.Emission.Radius = MathF.Max(0.01f, v));
                    EditorGUI.Toggle(paper, $"{id}_emit_shell", "Emit From Shell", ps.Emission.EmitFromShell)
                        .OnValueChanged(v => ps.Emission.EmitFromShell = v);
                    break;
                case EmissionShape.Box:
                    EditorGUI.Vector3Field(paper, $"{id}_emit_box", "Box Size", ps.Emission.BoxSize)
                        .OnValueChanged(v => ps.Emission.BoxSize = v);
                    break;
                case EmissionShape.Cone:
                    EditorGUI.FloatField(paper, $"{id}_emit_rad2", ps.Emission.Radius, "Radius")
                        .OnValueChanged(v => ps.Emission.Radius = MathF.Max(0.01f, v));
                    EditorGUI.FloatField(paper, $"{id}_emit_angle", ps.Emission.ConeAngle, "Angle")
                        .OnValueChanged(v => ps.Emission.ConeAngle = MathF.Max(0f, MathF.Min(90f, v)));
                    break;
            }

            paper.Box($"{id}_emit_sp2").Height(4);

            // Bursts
            DrawBurstList(paper, $"{id}_emit_bursts", ps.Emission.Bursts, font);
        });

        // Size Over Lifetime
        DrawModule(paper, $"{id}_sol", "Size over Lifetime", EditorIcons.ArrowsLeftRight, ps.SizeOverLifetime, font, () =>
        {
            PropertyGrid.DrawField(paper, $"{id}_sol_c", "Size", typeof(MinMaxCurve), ps.SizeOverLifetime.SizeCurve,
                v => ps.SizeOverLifetime.SizeCurve = v as MinMaxCurve ?? new MinMaxCurve(1f), 0);
        });

        // Color Over Lifetime
        DrawModule(paper, $"{id}_col", "Color over Lifetime", EditorIcons.Palette, ps.ColorOverLifetime, font, () =>
        {
            PropertyGrid.DrawField(paper, $"{id}_col_g", "Color", typeof(MinMaxGradient), ps.ColorOverLifetime.ColorGradient,
                v => ps.ColorOverLifetime.ColorGradient = v as MinMaxGradient ?? new MinMaxGradient(), 0);
        });

        // Rotation Over Lifetime
        DrawModule(paper, $"{id}_rol", "Rotation over Lifetime", EditorIcons.ArrowsSpin, ps.RotationOverLifetime, font, () =>
        {
            PropertyGrid.DrawField(paper, $"{id}_rol_av", "Angular Velocity", typeof(MinMaxCurve), ps.RotationOverLifetime.AngularVelocity,
                v => ps.RotationOverLifetime.AngularVelocity = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
        });

        // Velocity Over Lifetime
        DrawModule(paper, $"{id}_vol", "Velocity over Lifetime", EditorIcons.Gauge, ps.VelocityOverLifetime, font, () =>
        {
            PropertyGrid.DrawField(paper, $"{id}_vol_x", "Velocity X", typeof(MinMaxCurve), ps.VelocityOverLifetime.VelocityX,
                v => ps.VelocityOverLifetime.VelocityX = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
            PropertyGrid.DrawField(paper, $"{id}_vol_y", "Velocity Y", typeof(MinMaxCurve), ps.VelocityOverLifetime.VelocityY,
                v => ps.VelocityOverLifetime.VelocityY = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
            PropertyGrid.DrawField(paper, $"{id}_vol_z", "Velocity Z", typeof(MinMaxCurve), ps.VelocityOverLifetime.VelocityZ,
                v => ps.VelocityOverLifetime.VelocityZ = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
        });

        // Collision
        DrawModule(paper, $"{id}_coll", "Collision", EditorIcons.Explosion, ps.Collision, font, () =>
        {
            EditorGUI.EnumDropdown(paper, $"{id}_coll_mode", "Mode", ps.Collision.Mode)
                .OnValueChanged(v => ps.Collision.Mode = v);
            EditorGUI.EnumDropdown(paper, $"{id}_coll_qual", "Quality", ps.Collision.Quality)
                .OnValueChanged(v => ps.Collision.Quality = v);
            EditorGUI.FloatField(paper, $"{id}_coll_damp", ps.Collision.Dampen, "Dampen")
                .OnValueChanged(v => ps.Collision.Dampen = MathF.Max(0f, MathF.Min(1f, v)));
            EditorGUI.FloatField(paper, $"{id}_coll_bounce", ps.Collision.Bounce, "Bounce")
                .OnValueChanged(v => ps.Collision.Bounce = MathF.Max(0f, MathF.Min(1f, v)));
            EditorGUI.FloatField(paper, $"{id}_coll_ll", ps.Collision.LifetimeLoss, "Lifetime Loss")
                .OnValueChanged(v => ps.Collision.LifetimeLoss = MathF.Max(0f, v));
            EditorGUI.FloatField(paper, $"{id}_coll_mks", ps.Collision.MinKillSpeed, "Min Kill Speed")
                .OnValueChanged(v => ps.Collision.MinKillSpeed = MathF.Max(0f, v));
            EditorGUI.FloatField(paper, $"{id}_coll_rad", ps.Collision.ParticleRadius, "Particle Radius")
                .OnValueChanged(v => ps.Collision.ParticleRadius = MathF.Max(0.001f, v));
            EditorGUI.FloatField(paper, $"{id}_coll_dist", ps.Collision.MaxCollisionDistance, "Max Distance")
                .OnValueChanged(v => ps.Collision.MaxCollisionDistance = MathF.Max(0.01f, v));

            PropertyGrid.DrawField(paper, $"{id}_coll_lm", "Collides With", typeof(LayerMask), ps.Collision.CollidesWith,
                v => ps.Collision.CollidesWith = (LayerMask)v!, 0);

            if (ps.Collision.Mode == CollisionMode.World)
            {
                EditorGUI.FloatField(paper, $"{id}_coll_vox", ps.Collision.VoxelSize, "Voxel Size")
                    .OnValueChanged(v => ps.Collision.VoxelSize = MathF.Max(0.1f, v));
            }
        });

        // UV Animation
        DrawModule(paper, $"{id}_uv", "UV Animation", EditorIcons.Film, ps.UV, font, () =>
        {
            EditorGUI.EnumDropdown(paper, $"{id}_uv_mode", "Mode", ps.UV.Mode)
                .OnValueChanged(v => ps.UV.Mode = v);

            if (ps.UV.Mode == UVAnimationMode.GridAnimation)
            {
                EditorGUI.IntField(paper, $"{id}_uv_tx", ps.UV.TilesX, "Tiles X")
                    .OnValueChanged(v => ps.UV.TilesX = Math.Max(1, v));
                EditorGUI.IntField(paper, $"{id}_uv_ty", ps.UV.TilesY, "Tiles Y")
                    .OnValueChanged(v => ps.UV.TilesY = Math.Max(1, v));
                EditorGUI.IntField(paper, $"{id}_uv_cc", ps.UV.CycleCount, "Cycle Count")
                    .OnValueChanged(v => ps.UV.CycleCount = Math.Max(1, v));
                EditorGUI.FloatField(paper, $"{id}_uv_fot", ps.UV.FrameOverTime, "Frame Over Time")
                    .OnValueChanged(v => ps.UV.FrameOverTime = MathF.Max(0f, v));
                EditorGUI.Toggle(paper, $"{id}_uv_rsf", "Random Start Frame", ps.UV.RandomStartFrame)
                    .OnValueChanged(v => ps.UV.RandomStartFrame = v);
            }
            else
            {
                PropertyGrid.DrawField(paper, $"{id}_uv_uo", "U Offset", typeof(MinMaxCurve), ps.UV.UOffsetCurve,
                    v => ps.UV.UOffsetCurve = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
                PropertyGrid.DrawField(paper, $"{id}_uv_vo", "V Offset", typeof(MinMaxCurve), ps.UV.VOffsetCurve,
                    v => ps.UV.VOffsetCurve = v as MinMaxCurve ?? new MinMaxCurve(0f), 0);
                EditorGUI.Vector2Field(paper, $"{id}_uv_ss", "Scroll Speed", ps.UV.ScrollSpeed)
                    .OnValueChanged(v => ps.UV.ScrollSpeed = v);
            }

            EditorGUI.Toggle(paper, $"{id}_uv_fu", "Flip U", ps.UV.FlipU)
                .OnValueChanged(v => ps.UV.FlipU = v);
            EditorGUI.Toggle(paper, $"{id}_uv_fv", "Flip V", ps.UV.FlipV)
                .OnValueChanged(v => ps.UV.FlipV = v);
        });
    }

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
                EditorGUI.Button(paper, $"{id}_pause", $"{EditorIcons.Pause}  Pause", width: 70)
                    .OnValueChanged(_ => ps.Pause());
                EditorGUI.Button(paper, $"{id}_stop", $"{EditorIcons.Stop}  Stop", width: 65)
                    .OnValueChanged(_ => ps.Stop());
            }
            else
            {
                EditorGUI.Button(paper, $"{id}_play", $"{EditorIcons.Play}  Play", width: 65)
                    .OnValueChanged(_ => ps.Play());
            }

            EditorGUI.Button(paper, $"{id}_clear", $"{EditorIcons.Trash}  Clear", width: 70)
                .OnValueChanged(_ => ps.Clear());

            paper.Box($"{id}_spacer").Width(UnitValue.Stretch());

            paper.Box($"{id}_count")
                .Width(UnitValue.Auto).Height(EditorTheme.RowHeight)
                .Text($"Particles: {ps.ParticleCount}", font).TextColor(EditorTheme.Ink400)
                .FontSize(fs - 2).Alignment(TextAlignment.MiddleRight);
        }
    }

    // ================================================================
    //  Module Header (collapsible with enable toggle)
    // ================================================================

    private static bool DrawModuleHeader(Paper paper, string id, string label, ParticleSystemModule? module, Prowl.Scribe.FontFile font, bool forceEnabled)
    {
        var header = paper
            .Row(id)
            .Height(EditorTheme.RowHeight)
            .ChildLeft(4).RowBetween(4)
            .BackgroundColor(EditorTheme.Neutral300)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .Rounded(3);

        bool expanded = paper.GetElementStorage(header._handle, "exp", false);

        header.OnClick(e => paper.SetElementStorage(header._handle, "exp", !expanded));

        using (header.Enter())
        {
            // Expand arrow
            paper.Box($"{id}_arr")
                .Width(14).Height(EditorTheme.RowHeight)
                .Text(expanded ? EditorIcons.AngleDown : EditorIcons.AngleRight, font)
                .TextColor(EditorTheme.Ink400).FontSize(9f).Alignment(TextAlignment.MiddleCenter);

            // Enable toggle (if module, not main)
            if (module != null && !forceEnabled)
            {
                EditorGUI.Toggle(paper, $"{id}_en", "", module.Enabled)
                    .OnValueChanged(v => module.Enabled = v);
            }

            // Label
            paper.Box($"{id}_lbl")
                .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text(label, font)
                .TextColor(module != null && !module.Enabled && !forceEnabled ? EditorTheme.Ink300 : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
        }

        return expanded;
    }

    private static void DrawModule(Paper paper, string id, string label, string icon, ParticleSystemModule module, Prowl.Scribe.FontFile font, Action drawContents)
    {
        bool expanded = DrawModuleHeader(paper, $"{id}_h", $"{icon}  {label}", module, font, false);

        if (expanded)
        {
            using (paper.Column($"{id}_body")
                .Height(UnitValue.Auto)
                .Margin(8, 0, 4, 4)
                .Enter())
            {
                drawContents();
            }
        }
    }

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
                .ChildLeft(6).ChildRight(6).ChildTop(3).ChildBottom(3)
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

                EditorGUI.FloatField(paper, $"{id}_bt{i}", burst.Time, "Time")
                    .OnValueChanged(v => { burst.Time = MathF.Max(0f, v); bursts[idx] = burst; });
                EditorGUI.IntField(paper, $"{id}_bmn{i}", burst.MinCount, "Min Count")
                    .OnValueChanged(v => { burst.MinCount = Math.Max(0, v); bursts[idx] = burst; });
                EditorGUI.IntField(paper, $"{id}_bmx{i}", burst.MaxCount, "Max Count")
                    .OnValueChanged(v => { burst.MaxCount = Math.Max(burst.MinCount, v); bursts[idx] = burst; });
                EditorGUI.IntField(paper, $"{id}_bcc{i}", burst.CycleCount, "Cycles")
                    .OnValueChanged(v => { burst.CycleCount = Math.Max(0, v); bursts[idx] = burst; });
                EditorGUI.FloatField(paper, $"{id}_bri{i}", burst.RepeatInterval, "Repeat Interval")
                    .OnValueChanged(v => { burst.RepeatInterval = MathF.Max(0f, v); bursts[idx] = burst; });
            }
        }

        EditorGUI.Button(paper, $"{id}_add", $"{EditorIcons.Plus}  Add Burst", width: 110)
            .OnValueChanged(_ => bursts.Add(new ParticleBurst { Time = 0f, MinCount = 10, MaxCount = 10, CycleCount = 1, RepeatInterval = 0.01f }));
    }
}
