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
    private int _expandedEventIndex = -1;

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

        if (EditorAssetBackend.Instance is { } db && db.IsImportedAnimationSubAsset(clip.AssetID, out AssetEntry? parentModel) && parentModel != null)
        {
            EditorGUI.Note(
                paper,
                $"{id}_imp_note",
                $"Imported from '{parentModel.Path}'. Events & Loop save to the model's .meta and survive reimports; curve/track changes apply until the model is reimported (or click Extract below for a standalone .animclip).");
        }

        // Settings group
        EditorGUI.SectionHeader(paper, $"{id}_h_settings", "Settings", first: true);

        EditorGUI.Row(paper, $"{id}_fps", "Frame Rate", () =>
            Origami.NumericField<float>(paper, $"{id}_fps_v", clip.FrameRate, v =>
            {
                clip.FrameRate = Math.Clamp(v, 1f, 240f);
                clip.Invalidate();
                EditorUtility.SetDirty(clip);
            }).Min(1f).Max(240f).Format("0").Suffix("fps").Show());

        EditorGUI.Row(paper, $"{id}_wrap", "Wrap Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_wrap_v", clip.WrapMode, v =>
            {
                clip.WrapMode = v;
                clip.Loop = v is SceneClipWrapMode.Loop or SceneClipWrapMode.PingPong;
                clip.Invalidate();
                EditorUtility.SetDirty(clip);
            }).Show());

        EditorGUI.Row(paper, $"{id}_comp_mode", "Completion Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_comp_mode_v", clip.CompletionMode, v =>
            {
                clip.CompletionMode = v;
                clip.Invalidate();
                EditorUtility.SetDirty(clip);
            }).Show());

        EditorGUI.SettingsToggle(paper, $"{id}_loop", "Loop", clip.Loop, v =>
        {
            clip.Loop = v;
            clip.WrapMode = v ? SceneClipWrapMode.Loop : SceneClipWrapMode.Once;
            clip.Invalidate();
            EditorUtility.SetDirty(clip);
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
                                    if (idx >= 0 && idx < clip.CurveTracks.Count)
                                    {
                                        Undo.RecordObject(clip, "Remove Curve Track");
                                        clip.CurveTracks.RemoveAt(idx);
                                        clip.Invalidate();
                                        EditorUtility.SetDirty(clip);
                                    }
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
                                    if (idx >= 0 && idx < clip.ObjectTracks.Count)
                                    {
                                        Undo.RecordObject(clip, "Remove Object Track");
                                        clip.ObjectTracks.RemoveAt(idx);
                                        clip.Invalidate();
                                        EditorUtility.SetDirty(clip);
                                    }
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
                                    if (idx >= 0 && idx < clip.StringTracks.Count)
                                    {
                                        Undo.RecordObject(clip, "Remove String Track");
                                        clip.StringTracks.RemoveAt(idx);
                                        clip.Invalidate();
                                        EditorUtility.SetDirty(clip);
                                    }
                                });
                        }
                    }
                }
            }
        }

        // Property Events summary + inline editor
        if (_expandedEventIndex >= clip.PropertyEvents.Count)
            _expandedEventIndex = -1;

        EditorGUI.SectionHeader(paper, $"{id}_h_events", $"Animation Events ({clip.PropertyEvents.Count})");
        using (paper.Column($"{id}_events").Height(UnitValue.Auto).Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(4).Enter())
        {
            for (int i = 0; i < clip.PropertyEvents.Count; i++)
            {
                SceneAnimationEvent ev = clip.PropertyEvents[i];
                int idx = i;
                bool expanded = _expandedEventIndex == idx;
                string targetLabel = string.IsNullOrEmpty(ev.RelativePath) ? "(Root)" : ev.RelativePath;
                string timeLabel = ev.IsRange ? $"{ev.Time:0.00}-{ev.EndTime:0.00}s" : $"{ev.Time:0.00}s";
                string fnLabel = FormatEventSummary(ev);
                bool unassigned = !ev.IsRange
                    ? string.IsNullOrEmpty(ev.FunctionName)
                    : string.IsNullOrEmpty(ev.FunctionName) && string.IsNullOrEmpty(ev.StayFunctionName) && string.IsNullOrEmpty(ev.ExitFunctionName);

                using (paper.Column($"{id}_ev_card_{i}").Height(UnitValue.Auto)
                    .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Neutral300).Enter())
                {
                    using (paper.Row($"{id}_ev_{i}").Height(28).Padding(8, 8, 0, 0).Gap(6)
                        .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                        .AlignItems(LayoutAlignment.Center)
                        .Tooltip("Click to expand inline event setup")
                        .OnClick(0, (_, _) => _expandedEventIndex = expanded ? -1 : idx)
                        .Enter())
                    {
                        paper.Box($"{id}_ev_{i}_chv").Width(12).Height(28).IsNotInteractable()
                            .Text(expanded ? EditorIcons.ChevronDown : EditorIcons.ChevronRight, font).TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSizeSmall * 0.85f).Alignment(TextAlignment.MiddleCenter);

                        paper.Box($"{id}_ev_{i}_t").Width(82).Height(28).IsNotInteractable()
                            .Text(timeLabel, EditorTheme.FontMono ?? font).TextColor(ev.IsRange ? EditorTheme.Purple400 : EditorTheme.Amber400)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                        paper.Box($"{id}_ev_{i}_l").Width(UnitValue.Stretch()).Height(28).IsNotInteractable()
                            .Text($"{targetLabel} -> {fnLabel}", font).TextColor(unassigned ? EditorTheme.Amber400 : EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                        paper.Box($"{id}_ev_{i}_win").Width(20).Height(20).Rounded(m.SmallRounding)
                            .Hovered.BackgroundColor(EditorTheme.Neutral500).End()
                            .Text(EditorIcons.Film, font).TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                            .Tooltip("Open in Animation Window")
                            .OnClick(0, (_, _) => AnimationPanel.OpenClipEvent(clip, idx));

                        if (!Origami.IsReadOnly)
                        {
                            paper.Box($"{id}_ev_{i}_del").Width(20).Height(20).Rounded(m.SmallRounding)
                                .Hovered.BackgroundColor(EditorTheme.Neutral500).End()
                                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                                .Tooltip("Remove event")
                                .OnClick(0, (_, _) =>
                                {
                                    if (idx >= 0 && idx < clip.PropertyEvents.Count)
                                    {
                                        Undo.RecordObject(clip, "Remove Animation Event");
                                        clip.PropertyEvents.RemoveAt(idx);
                                        if (_expandedEventIndex == idx) _expandedEventIndex = -1;
                                        clip.Invalidate();
                                        EditorUtility.SetDirty(clip);
                                    }
                                });
                        }
                    }

                    if (expanded && !Origami.IsReadOnly)
                    {
                        DrawInlineEventEditor(paper, $"{id}_ev_ed_{i}", clip, ev);
                    }
                }
            }

            if (!Origami.IsReadOnly)
            {
                using (paper.Row($"{id}_ev_add_row").Height(26).Gap(6).Margin(0, 0, clip.PropertyEvents.Count > 0 ? 2 : 0, 0).Enter())
                {
                    Origami.Button(paper, $"{id}_ev_add", $"{EditorIcons.Plus}  Add Event", () =>
                    {
                        Undo.RecordObject(clip, "Add Animation Event");
                        clip.PropertyEvents.Add(new SceneAnimationEvent { Time = 0f });
                        _expandedEventIndex = clip.PropertyEvents.Count - 1;
                        clip.Invalidate();
                        EditorUtility.SetDirty(clip);
                    }).Width(UnitValue.Stretch()).Height(24).Show();

                    Origami.Button(paper, $"{id}_ev_add_rng", $"{EditorIcons.Plus}  Add Ranged Event", () =>
                    {
                        Undo.RecordObject(clip, "Add Ranged Animation Event");
                        float defaultEnd = Math.Max(0.25f, Math.Min(0.5f, clip.Duration > 0f ? clip.Duration : 0.5f));
                        clip.PropertyEvents.Add(new SceneAnimationEvent { Time = 0f, EndTime = defaultEnd });
                        _expandedEventIndex = clip.PropertyEvents.Count - 1;
                        clip.Invalidate();
                        EditorUtility.SetDirty(clip);
                    }).Width(UnitValue.Stretch()).Height(24).Show();
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

    private static string FormatEventSummary(SceneAnimationEvent ev)
    {
        string FormatCall(string fn)
        {
            if (string.IsNullOrEmpty(fn)) return "<Select Function>";
            return ev.ArgType switch
            {
                ProwlActionArgType.Float => $"{fn}({ev.FloatParam:0.##})",
                ProwlActionArgType.Int => $"{fn}({ev.IntParam})",
                ProwlActionArgType.Bool => $"{fn}({(ev.BoolParam ? "true" : "false")})",
                ProwlActionArgType.String => $"{fn}(\"{ev.StringParam}\")",
                ProwlActionArgType.Object => $"{fn}({(ev.ObjectParam.IsValid() ? ev.ObjectParam!.Name : "None")})",
                _ => $"{fn}()",
            };
        }

        if (!ev.IsRange)
            return FormatCall(ev.FunctionName);

        string enter = string.IsNullOrEmpty(ev.FunctionName) ? "-" : FormatCall(ev.FunctionName);
        string stay = string.IsNullOrEmpty(ev.StayFunctionName) ? "" : $" | Stay:{ev.StayFunctionName}";
        string exit = string.IsNullOrEmpty(ev.ExitFunctionName) ? "-" : $"{ev.ExitFunctionName}()";
        return $"Enter:{enter}{stay} | Exit:{exit}";
    }

    private static void DrawInlineEventEditor(Paper paper, string id, AnimationClip clip, SceneAnimationEvent ev)
    {
        bool isRanged = ev.IsRange;
        using (paper.Column(id).Height(UnitValue.Auto).Padding(10, 10, 4, 8).Gap(4)
            .BackgroundColor(EditorTheme.Neutral200).Enter())
        {
            using (paper.Row($"{id}_mode").Height(22).Gap(6).AlignItems(LayoutAlignment.Center).Enter())
            {
                EditorGUI.Chip(paper, $"{id}_inst", "Instant", !isRanged, () =>
                {
                    if (!isRanged) return;
                    Undo.RecordObject(clip, "Set Instant Event Mode");
                    ev.EndTime = ev.Time;
                    clip.Invalidate();
                    EditorUtility.SetDirty(clip);
                }, height: 19f);

                EditorGUI.Chip(paper, $"{id}_rng", "Ranged (Enter / Exit)", isRanged, () =>
                {
                    if (isRanged) return;
                    Undo.RecordObject(clip, "Set Ranged Event Mode");
                    ev.EndTime = ev.Time + Math.Max(0.25f, 1f / Math.Max(1f, clip.FrameRate));
                    clip.Invalidate();
                    EditorUtility.SetDirty(clip);
                }, height: 19f);
            }

            EditorGUI.Row(paper, $"{id}_t0", isRanged ? "Start Time" : "Time", () =>
                Origami.NumericField<float>(paper, $"{id}_t0_v", ev.Time, v =>
                {
                    Undo.RecordObject(clip, "Edit Event Start Time");
                    float span = Math.Max(0.01f, ev.Duration);
                    ev.Time = Math.Max(0f, v);
                    ev.EndTime = isRanged ? Math.Max(ev.Time + 0.01f, ev.EndTime > ev.Time ? ev.EndTime : ev.Time + span) : ev.Time;
                    clip.Invalidate();
                    EditorUtility.SetDirty(clip);
                }).Format("0.###").Suffix("s").Show());

            if (isRanged)
            {
                EditorGUI.Row(paper, $"{id}_t1", "End Time", () =>
                    Origami.NumericField<float>(paper, $"{id}_t1_v", ev.EndTime, v =>
                    {
                        Undo.RecordObject(clip, "Edit Event End Time");
                        ev.EndTime = Math.Max(ev.Time + 0.01f, v);
                        clip.Invalidate();
                        EditorUtility.SetDirty(clip);
                    }).Format("0.###").Suffix("s").Show());
            }

            EditorGUI.Row(paper, $"{id}_tgt", "Target Path", () =>
                Origami.TextField(paper, $"{id}_tgt_v", ev.RelativePath, v =>
                {
                    Undo.RecordObject(clip, "Edit Event Target Path");
                    ev.RelativePath = v.Trim();
                    EditorUtility.SetDirty(clip);
                }).Placeholder("(Root GameObject)").CommitOnEnter().Show());

            EditorGUI.Row(paper, $"{id}_fn", isRanged ? "Enter Method" : "Function", () =>
                Origami.TextField(paper, $"{id}_fn_v", ev.FunctionName, v =>
                {
                    Undo.RecordObject(clip, "Edit Event Function");
                    ev.FunctionName = v.Trim();
                    EditorUtility.SetDirty(clip);
                }).Placeholder("MethodName").CommitOnEnter().Show());

            if (isRanged)
            {
                EditorGUI.Row(paper, $"{id}_stay", "Stay Method", () =>
                    Origami.TextField(paper, $"{id}_stay_v", ev.StayFunctionName, v =>
                    {
                        Undo.RecordObject(clip, "Edit Event Stay Method");
                        ev.StayFunctionName = v.Trim();
                        EditorUtility.SetDirty(clip);
                    }).Placeholder("Optional (float 0..1)").CommitOnEnter().Show());

                EditorGUI.Row(paper, $"{id}_exit", "Exit Method", () =>
                    Origami.TextField(paper, $"{id}_exit_v", ev.ExitFunctionName, v =>
                    {
                        Undo.RecordObject(clip, "Edit Event Exit Method");
                        ev.ExitFunctionName = v.Trim();
                        EditorUtility.SetDirty(clip);
                    }).Placeholder("ExitMethodName").CommitOnEnter().Show());
            }
        }
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
