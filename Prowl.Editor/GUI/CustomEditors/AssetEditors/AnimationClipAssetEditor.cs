// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(AnimationClip))]
public class AnimationClipAssetEditor : AssetImporterEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null || asset is not AnimationClip clip) return;

        OrigamiMetrics m = Origami.Current.Metrics;
        if (!Origami.IsReadOnly) Undo.WatchObject(clip, "Edit Animation Clip");

        // Top action row: Open in Animation Window + Unpack Curves (if skeletal/humanoid)
        using (paper.Row($"{id}_top").Height(34).Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(m.SpacingMedium).Enter())
        {
            Origami.Button(paper, $"{id}_open", $"{EditorIcons.Film}  Open in Animation Window", () =>
            {
                AnimationPanel.OpenClip(clip);
            }).Primary().Width(UnitValue.Stretch()).Height(32).Show();

            if (!Origami.IsReadOnly && clip.HasMotionPayload)
            {
                Origami.Button(paper, $"{id}_unpack", $"{EditorIcons.Sliders}  Unpack Rig Curves", () =>
                {
                    Undo.RecordObject(clip, "Unpack Rig Curves");
                    clip.UnpackMotionToCurveTracks();
                    EditorUtility.SetDirty(clip);
                }).Width(UnitValue.Auto).Height(32).Show();
            }
        }

        // Settings group
        EditorGUI.SectionHeader(paper, $"{id}_h_settings", "Settings", first: true);

        EditorGUI.Row(paper, $"{id}_fps", "Frame Rate", () =>
            Origami.NumericField<float>(paper, $"{id}_fps_v", clip.FrameRate, v =>
            {
                clip.FrameRate = Math.Clamp(v, 1f, 240f);
                clip.Invalidate();
            }).Min(1f).Max(240f).Format("0").Suffix("fps").Show());

        EditorGUI.Row(paper, $"{id}_wrap", "Wrap Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_wrap_v", clip.WrapMode, v =>
            {
                clip.WrapMode = v;
                clip.Loop = v is SceneClipWrapMode.Loop or SceneClipWrapMode.PingPong;
                clip.Invalidate();
            }).Show());

        EditorGUI.Row(paper, $"{id}_comp_mode", "Completion Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_comp_mode_v", clip.CompletionMode, v =>
            {
                clip.CompletionMode = v;
                clip.Invalidate();
            }).Show());

        EditorGUI.SettingsToggle(paper, $"{id}_loop", "Loop", clip.Loop, v =>
        {
            clip.Loop = v;
            clip.WrapMode = v ? SceneClipWrapMode.Loop : SceneClipWrapMode.Once;
            clip.Invalidate();
        }, separator: false);

        // Stats chips
        using (paper.Row($"{id}_stats").Height(26).Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingLarge).Gap(6).Enter())
        {
            EditorGUI.StatChip(paper, $"{id}_st_kind", clip.Kind.ToString(), font);
            EditorGUI.StatChip(paper, $"{id}_st_dur", $"{clip.Duration:0.00}s", font);
            int totalTracks = clip.CurveTracks.Count + clip.ObjectTracks.Count + clip.StringTracks.Count;
            EditorGUI.StatChip(paper, $"{id}_st_tr", totalTracks == 1 ? "1 track" : $"{totalTracks} tracks", font);
            int totalEvents = clip.Events.Count + clip.PropertyEvents.Count;
            EditorGUI.StatChip(paper, $"{id}_st_ev", totalEvents == 1 ? "1 event" : $"{totalEvents} events", font);
        }

        // Curve Tracks summary
        if (clip.CurveTracks.Count > 0)
        {
            EditorGUI.SectionHeader(paper, $"{id}_h_curves", $"Property Curve Tracks ({clip.CurveTracks.Count})");
            using (paper.Column($"{id}_curves").Height(UnitValue.Auto).Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(4).Enter())
            {
                for (int i = 0; i < clip.CurveTracks.Count; i++)
                {
                    SceneCurveTrack track = clip.CurveTracks[i];
                    int idx = i;
                    string label = FormatTrackLabel(track.RelativePath, track.ComponentType, track.PropertyPath);
                    using (paper.Row($"{id}_ct_{i}").Height(28).Padding(8, 8, 0, 0).Gap(8)
                        .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Neutral300)
                        .AlignItems(LayoutAlignment.Center).Enter())
                    {
                        paper.Box($"{id}_ct_{i}_l").Width(UnitValue.Stretch()).Height(28).IsNotInteractable()
                            .Text(label, font).TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                        paper.Box($"{id}_ct_{i}_k").Width(UnitValue.Auto).Height(28).IsNotInteractable()
                            .Text($"{track.Curve.Count} keys", font).TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

                        if (!Origami.IsReadOnly)
                        {
                            paper.Box($"{id}_ct_{i}_del").Width(20).Height(20).Rounded(m.SmallRounding)
                                .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                                .Tooltip("Remove track")
                                .OnClick(0, (_, _) =>
                                {
                                    clip.CurveTracks.RemoveAt(idx);
                                    clip.Invalidate();
                                });
                        }
                    }
                }
            }
        }

        // Object Tracks summary
        if (clip.ObjectTracks.Count > 0)
        {
            EditorGUI.SectionHeader(paper, $"{id}_h_objs", $"Object Tracks ({clip.ObjectTracks.Count})");
            using (paper.Column($"{id}_objs").Height(UnitValue.Auto).Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(4).Enter())
            {
                for (int i = 0; i < clip.ObjectTracks.Count; i++)
                {
                    SceneObjectTrack track = clip.ObjectTracks[i];
                    int idx = i;
                    string label = FormatTrackLabel(track.RelativePath, track.ComponentType, track.PropertyPath);
                    using (paper.Row($"{id}_ot_{i}").Height(28).Padding(8, 8, 0, 0).Gap(8)
                        .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Neutral300)
                        .AlignItems(LayoutAlignment.Center).Enter())
                    {
                        paper.Box($"{id}_ot_{i}_l").Width(UnitValue.Stretch()).Height(28).IsNotInteractable()
                            .Text(label, font).TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                        paper.Box($"{id}_ot_{i}_k").Width(UnitValue.Auto).Height(28).IsNotInteractable()
                            .Text($"{track.Keyframes.Count} keys", font).TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

                        if (!Origami.IsReadOnly)
                        {
                            paper.Box($"{id}_ot_{i}_del").Width(20).Height(20).Rounded(m.SmallRounding)
                                .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                                .Tooltip("Remove track")
                                .OnClick(0, (_, _) =>
                                {
                                    clip.ObjectTracks.RemoveAt(idx);
                                    clip.Invalidate();
                                });
                        }
                    }
                }
            }
        }

        // String Tracks summary
        if (clip.StringTracks.Count > 0)
        {
            EditorGUI.SectionHeader(paper, $"{id}_h_strs", $"String Tracks ({clip.StringTracks.Count})");
            using (paper.Column($"{id}_strs").Height(UnitValue.Auto).Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(4).Enter())
            {
                for (int i = 0; i < clip.StringTracks.Count; i++)
                {
                    SceneStringTrack track = clip.StringTracks[i];
                    int idx = i;
                    string label = FormatTrackLabel(track.RelativePath, track.ComponentType, track.PropertyPath);
                    using (paper.Row($"{id}_st_{i}").Height(28).Padding(8, 8, 0, 0).Gap(8)
                        .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Neutral300)
                        .AlignItems(LayoutAlignment.Center).Enter())
                    {
                        paper.Box($"{id}_st_{i}_l").Width(UnitValue.Stretch()).Height(28).IsNotInteractable()
                            .Text(label, font).TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                        paper.Box($"{id}_st_{i}_k").Width(UnitValue.Auto).Height(28).IsNotInteractable()
                            .Text($"{track.Keyframes.Count} keys", font).TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

                        if (!Origami.IsReadOnly)
                        {
                            paper.Box($"{id}_st_{i}_del").Width(20).Height(20).Rounded(m.SmallRounding)
                                .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                                .Tooltip("Remove track")
                                .OnClick(0, (_, _) =>
                                {
                                    clip.StringTracks.RemoveAt(idx);
                                    clip.Invalidate();
                                });
                        }
                    }
                }
            }
        }

        // Property Events summary
        if (clip.PropertyEvents.Count > 0)
        {
            EditorGUI.SectionHeader(paper, $"{id}_h_events", $"Animation Events ({clip.PropertyEvents.Count})");
            using (paper.Column($"{id}_events").Height(UnitValue.Auto).Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(4).Enter())
            {
                for (int i = 0; i < clip.PropertyEvents.Count; i++)
                {
                    SceneAnimationEvent ev = clip.PropertyEvents[i];
                    int idx = i;
                    string targetLabel = string.IsNullOrEmpty(ev.RelativePath) ? "(Root)" : ev.RelativePath;
                    string timeLabel = ev.IsRange ? $"{ev.Time:0.00}-{ev.EndTime:0.00}s" : $"{ev.Time:0.00}s";
                    string fnLabel = ev.IsRange
                        ? $"Enter:{(string.IsNullOrEmpty(ev.FunctionName) ? "-" : ev.FunctionName)} / Exit:{(string.IsNullOrEmpty(ev.ExitFunctionName) ? "-" : ev.ExitFunctionName)}"
                        : (string.IsNullOrEmpty(ev.FunctionName) ? "<unassigned>" : $"{ev.FunctionName}()");
                    using (paper.Row($"{id}_ev_{i}").Height(28).Padding(8, 8, 0, 0).Gap(8)
                        .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Neutral300)
                        .AlignItems(LayoutAlignment.Center).Enter())
                    {
                        paper.Box($"{id}_ev_{i}_t").Width(82).Height(28).IsNotInteractable()
                            .Text(timeLabel, EditorTheme.FontMono ?? font).TextColor(ev.IsRange ? EditorTheme.Purple400 : EditorTheme.Amber400)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                        paper.Box($"{id}_ev_{i}_l").Width(UnitValue.Stretch()).Height(28).IsNotInteractable()
                            .Text($"{targetLabel} -> {fnLabel}", font).TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                        if (!Origami.IsReadOnly)
                        {
                            paper.Box($"{id}_ev_{i}_del").Width(20).Height(20).Rounded(m.SmallRounding)
                                .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                                .Tooltip("Remove event")
                                .OnClick(0, (_, _) =>
                                {
                                    clip.PropertyEvents.RemoveAt(idx);
                                    clip.Invalidate();
                                });
                        }
                    }
                }
            }
        }

        if (!clip.HasMotionPayload && !clip.HasPropertyTracks)
            EditorGUI.Note(paper, $"{id}_empty", "This Animation Clip has no tracks yet. Select a GameObject with an Animator and open the Animation Window to add or record tracks.");

        DrawSaveRevertBar(paper, $"{id}_save", clip);
    }

    private static string FormatTrackLabel(string relativePath, string componentType, string propertyPath)
    {
        string obj = string.IsNullOrEmpty(relativePath) ? "" : $"{relativePath} : ";
        return $"{obj}{componentType}.{propertyPath}";
    }

    [AssetDoubleClickHandler(".animclip")]
    public static bool OnDoubleClick(string relativePath, Guid guid)
    {
        if (AssetDatabase.Load<AnimationClip>(guid) is { } clip && clip.IsValid())
        {
            AnimationPanel.OpenClip(clip);
            return true;
        }
        return false;
    }
}
