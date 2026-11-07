using System;

using Prowl.Editor.Core;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.Quill;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures.Generation;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Editor.GUI.EditorGUI;

namespace Prowl.Editor.GUI;

/// <summary>How the Timeline panel shows logs.</summary>
[EditorPreferences("Timeline", EditorIcons.Terminal, order: 20)]
public sealed class TimelinePreferences : EditorPreferences
{
    [Range(0, 32), Tooltip("Size of log text. 0 follows the small UI text size.")]
    public float FontSize;
}

public class TimelinePanel : DockPanel
{
    private Timeline? _timeline;
    private float _trackListWidth = 400;

    public static TimelinePanel Instance { get; } = new();

    [MenuItem("Window/General/Timeline", priority: 0)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(TimelinePanel));

    /// <summary>
    /// The title of the panel, as shown in the tab header.
    /// </summary>
    public override string Title => Loc.Get("panel.timeline");

    /// <summary>
    /// The panel icon, as shown in the tab header.
    /// </summary>
    public override string Icon => EditorIcons.Timeline;

    /// <summary>
    /// The 
    /// </summary>
    public double ViewStart { get; private set; }
    public double ViewLength { get; private set; } = 10;
    public double ViewEnd => ViewStart + ViewLength;
    public int SelectedTrackIndex = -1;

    /// <summary>Shows a graph in the first window not locked to something else.</summary>
    public static void OpenFor(Guid assetGuid)
    {
        var panel = EditorApplication.Instance?.FindOpenPanel(typeof(TimelinePanel)) as TimelinePanel;
        if (panel is null)
            Open();
        panel = EditorApplication.Instance?.FindOpenPanel(typeof(TimelinePanel)) as TimelinePanel;
        panel.Load(assetGuid);

        //if (Instance is not null)
        //{
        //    Instance.SwitchTo(assetGuid);
        //    return;
        //}
        ////foreach (TWindow window in OpenWindows)
        ////{
        ////    if (window._locked) continue;
        ////
        ////    window._parents.Clear();
        ////    if (window._assetGuid != assetGuid) window.SwitchTo(assetGuid);
        ////    EditorApplication.Instance?.FocusPanelInstance(window);
        ////    return;
        ////}
        ////
        //AssetDatabase.Load<Timeline>(assetGuid);
        //
        //var panel = new TimelinePanel();
        //panel.Load(assetGuid);
        //EditorApplication.Instance?.OpenPanelInstance(panel, 1240, 780);
    }

    public void Load(Guid assetGuid)
    {
        // Load the timeline asset and prepare the panel for display.
        _timeline = AssetDatabase.Load<Timeline>(assetGuid);
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Create a box that covers the entire panel client area.
        using IDisposable container = paper.Row("__timelinePanel")
            .Size(width, height)
            .PaddingTop(6)
            .Enter();

        // If no timeline is open, show a message.
        if (_timeline.IsNotValid())
        {
            paper.Box("no_timeline_loaded")
                .Size(width, height)
                .Alignment(PaperUI.TextAlignment.MiddleCenter)
                .Text("No timeline loaded.", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall);
            return;
        }

        const float headerHeight = 33;
        const float rowHeight = 33;

        // The left side, containing toolbar buttons and then track names.
        using (paper.Column("tl_track_names")
            .Size(_trackListWidth, height)
            .Clip()
            .Enter())
        {
            using (paper.Row("tl_controls")
                .Height(headerHeight)
                .Padding(10, 8, 0, 0)
                .Gap(4)
                .Enter())
            {
                Origami.Button(paper, "tl_add_track", Loc.Get("timeline.add_track"), () => ShowAddTrackMenu(paper)).Show();

                // Seek to beginning.
                ToolbarIconBtn(paper, "tl_seek_start", EditorIcons.Backward, Loc.Get("timeline.go_to_beginning"), false, () => { });

                // Play / pause
                ToolbarIconBtn(paper, "tl_play",
                    _timeline.IsPlaying ? EditorIcons.Pause : EditorIcons.Play,
                    _timeline.IsPlaying ? Loc.Get("timeline.pause") : Loc.Get("timeline.play"), false, () =>
                {
                    _timeline.IsPlaying = !_timeline.IsPlaying;
                });

                // Seek to end.
                ToolbarIconBtn(paper, "tl_seek_end", EditorIcons.Forward, Loc.Get("timeline.go_to_end"), false, () => { });

                Origami.TextField(paper, "tl_position", _timeline.PlaybackOffset.ToString(), (string text) =>
                {
                    if (double.TryParse(text, out var value))
                        _timeline.PlaybackOffset = (float)value;
                }).SelectAllOnFocus(true).Suffix("s").Height(headerHeight).Show();
            }
            Divider(paper, "tl_div1");

            // Track names
            int index = 0;
            foreach (TimelineTrack track in _timeline.Tracks)
            {
                using (paper.Row($"tl_track_{index}")
                    .Height(rowHeight)
                    .BackgroundColor(Selection.ActiveObject == track ? EditorTheme.Selected : EditorTheme.Glass)
                    .Margin(left: 0, right: 0, top: 2, bottom: 2)
                    .OnClick(e => Selection.Select(track))
                    .OnRightClick(e => ShowTrackContextMenu(paper, _timeline, track))
                    .Enter())
                {
                    paper.Box($"tl_track_name_{index}")
                        .PaddingLeft(10)
                        .Alignment(PaperUI.TextAlignment.MiddleLeft)
                        .Text(track.Name, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize);
                }
                index++;
            }

            // Dropping an audio clip below the list of tracks creates a new audio track.
            //bool isDraggingAudio = IsCompatibleDragTarget(typeof(AudioClip));
            //ElementBuilder audioDropTarget = paper.Box($"tl_new_audio_track")
            //    .Height(4)
            //    .BackgroundColor(IsCompatibleDragTarget(typeof(AudioClip)) ? EditorTheme.Accent : EditorTheme.Ink300)
            //    .Margin(left: 0, right: 0, top: 2, bottom: 2);
            //using (audioDropTarget.Enter())
            //{
            //    AssetDragPayload? drop = DragDrop.AcceptDrop<AssetDragPayload>(paper.IsParentHovered,
            //            dp => dp.AssetType != null && typeof(AudioClip).IsAssignableFrom(dp.AssetType));
            //    if (drop is not null)
            //    {
            //        var audioClip = (AudioClip)AssetDatabase.Get(drop.AssetGuid);
            //        var track = new TimelineAudioTrack();
            //        track.AddClip();
            //        _timeline.Tracks.Add(track);.AddClip();
            //    }
            //}
        }

        // Resizable divider.
        paper.Box("tl_vertical_divider")
            .Width(4)
            .BackgroundColor(EditorTheme.BorderSoft)
            .Hovered.BackgroundColor(EditorTheme.BorderStrong).End()
            .Cursor(PaperCursor.ResizeHorizontal)
            .OnDragStart(e => paper.SetElementStorage(nameof(_trackListWidth), _trackListWidth))
            .OnDragging(e =>
            {
                float initialTrackListWidth = paper.GetElementStorage<float>(nameof(_trackListWidth));
                _trackListWidth = float.Clamp(initialTrackListWidth + e.ScreenTotalDelta.X, 16, width - 16);
            });

        

        // Draw the timeline track header.

        // Draw the timeline track area.

        //Origami.ScrollView(paper, "tl_content", width, height - headerHeight).Body(() =>
        //{
        using (paper.Column("tl_track_list")
            .OnScroll(OnRulerScroll)
            .OnPostLayout((handle, rect) =>
            {
                paper.Draw((canvas, r) =>
                    DrawTrackListBackground(paper, canvas, r, font, headerHeight, rowHeight));
                

                //paper.Draw(ref handle, (canvas, r) =>
                //    {
                //        float headX = r.Min.X + 10; //ValueToCanvasX(_timeline.PlaybackOffset);
                //        canvas.BeginPath();
                //        canvas.SetStrokeColor(EditorTheme.Ink700);
                //        canvas.MoveTo(headX, r.Min.Y);
                //        canvas.LineTo(headX, r.Max.Y);
                //        canvas.Stroke();
                //    });
            })
            .Enter())
        {
            //paper.Box("tl_ruler")
            //    .Height(headerHeight)
            //    //.OnClick(_ => ClickTable(HitAt(paper)))
            //    //.OnHover(e => ClaimTooltip(e.Source, HitAt(paper)))
            //    .OnPostLayout((handle, rect) =>
            //    {
            //        paper.Draw(ref handle, (canvas, r) =>
            //            DrawRuler(canvas, r, font, fontSize: 12));
            //    });
            //Divider(paper, "tl_div2");

            // Tracks
            //int index = 0;
            //foreach (TimelineTrack track in _timeline.Tracks)
            //{
            //    using (paper.Row($"tl_track_{index}")
            //        .Height(rowHeight)
            //        .BackgroundColor(EditorTheme.Glass)
            //        .Margin(left: 0, right: 0, top: 2, bottom: 2)
            //        .Enter())
            //    {
            //        paper.Box($"tl_track_name_{index}")
            //            .PaddingLeft(10)
            //            .Alignment(PaperUI.TextAlignment.MiddleLeft)
            //            .Text(track.GetType().Name, font)
            //            .TextColor(EditorTheme.Ink400)
            //            .FontSize(EditorTheme.FontSize);
            //    }
            //    index++;
            //}
        }
        //});
    }

    private void OnRulerScroll(ScrollEvent e)
    {
        // Determine the zoom factor based on the scroll delta.
        const double sensitivity = 0.05;
        double zoomFactor = double.Max(1 - sensitivity * e.Delta, 0);

        // Calculate the new view length based on the zoom factor.
        double viewLength = ViewLength * zoomFactor;
        double deltaLength = ViewLength - viewLength;

        // Distribute the change using the normalized mouse position.
        ViewStart = double.Max(ViewStart + deltaLength * e.NormalizedPosition.X, 0);
        ViewLength = double.Min(viewLength, 1_000_000);  // Limit the maximum view length to 1 million seconds (11 days).
    }

    private void DrawTrackListBackground(Paper paper, Canvas canvas, Rect elementBounds, FontFile font, float headerHeight, float rowHeight)
    {
        const float tickMarkLabelFontSize = 12;

        // Pick the next highest power of ten to be our major step.
        const float targetPixelSpacing = 50; // Maximum tick label width
        int exponent = (int)double.Ceiling(double.Log10(ViewLength * (targetPixelSpacing / elementBounds.Size.X)));
        double majorStep = double.Max(double.Pow(10, exponent), 0.01);

        // Map a ruler value to its canvas X coordinate.
        float ValueToCanvasX(double value) => elementBounds.Min.X + (float)((value - ViewStart) / ViewLength * elementBounds.Size.X);

        // Map a canvas X coordinate to a ruler value.
        double CanvasXToValue(float x) => ((double)(x - elementBounds.Min.X) / elementBounds.Size.X) * ViewLength + ViewStart;

        // Detect if a value is a multiple of a given step, accounting for floating-point precision issues.
        bool IsMultipleOf(double value, double step) => double.Abs(double.Round(value / step) - (value / step)) < 1e-4;

        // Draw tick marks.
        double mediumStep = majorStep / 2f;
        double minorStep = mediumStep / 5f;
        for (double value = double.Floor(ViewStart / minorStep) * minorStep; value <= ViewEnd; value += minorStep)
        {
            // Calculate the y coordinate, width and color of the tick.
            float y, width;
            Color32 color;
            bool drawLabels;
            int labelDecimalPlaces;
            if (IsMultipleOf(value, majorStep))
            {
                // Major tick.
                drawLabels = true;
                y = elementBounds.Min.Y + 10;
                width = 2;
                color = EditorTheme.Ink200;
                labelDecimalPlaces = -exponent;
            }
            else if (IsMultipleOf(value, mediumStep))
            {
                // Medium tick.
                drawLabels = (mediumStep / ViewLength * elementBounds.Size.X) > targetPixelSpacing;
                y = drawLabels ? elementBounds.Min.Y + 10 : elementBounds.Min.Y + 12 + tickMarkLabelFontSize;
                width = 2;
                color = EditorTheme.Ink200;
                labelDecimalPlaces = 1 - exponent;
            }
            else
            {
                // Minor ticks.
                drawLabels = false;
                y = elementBounds.Min.Y + 10 + tickMarkLabelFontSize + 4;
                width = 1;
                color = EditorTheme.Ink200;
                labelDecimalPlaces = 1 - exponent;
            }

            // Calculate the X coordinate.
            float x = ValueToCanvasX(value);
            if (x < elementBounds.Min.X) continue;

            // Draw the tick mark.
            canvas.RectFilled(x - width/2, y, width, elementBounds.Min.Y + headerHeight - y, color);

            // Draw faint ticks behind the tracks.
            canvas.RectFilled(x - width / 2, elementBounds.Min.Y + headerHeight + 1, width, elementBounds.Max.Y - (elementBounds.Min.Y + headerHeight + 1), EditorTheme.BorderSoft);

            // Draw the tick label.
            if (drawLabels)
            {
                string label = labelDecimalPlaces <= 0 ? value.ToString("n0") : value.ToString($"f{labelDecimalPlaces}");
                canvas.DrawText(label, x + 5, y, EditorTheme.Ink300, tickMarkLabelFontSize, font);
            }
        }

        // Draw a horizontal divider.
        canvas.RectFilled(x: elementBounds.Min.X, y: elementBounds.Min.Y + headerHeight, elementBounds.Size.X, height: 1, EditorTheme.BorderSoft);

        // Draw tracks.
        int index = 0;
        foreach (TimelineTrack track in _timeline.Tracks)
        {
            // Determine the track bounds, and whether the mouse is inside those bounds.
            float y = elementBounds.Min.Y + headerHeight + 1 + index * (rowHeight + 4) + 2;
            Rect trackRect = new Rect(elementBounds.Min.X, y, elementBounds.Max.X, y + rowHeight);
            bool hover = paper.IsPointerOverRect(trackRect.Min.X, trackRect.Min.Y, trackRect.Size.X, trackRect.Size.Y);

            // Draw the track background.
            canvas.RectFilled(trackRect.Min.X, trackRect.Min.Y, trackRect.Size.X, trackRect.Size.Y,
                color: Selection.ActiveObject == track ? EditorTheme.Selected : EditorTheme.Glass);

            if (track is TimelineAudioTrack audioTrack)
            {
                // Outline the track if a clip is being dragged.
                if (IsCompatibleDragTarget(typeof(AudioClip)))
                    canvas.RectBorder(trackRect.Min.X, trackRect.Min.Y, trackRect.Size.X, trackRect.Size.Y, thickness: 2, EditorTheme.Accent);

                // Draw clips.
                foreach (TimelineAudioClip clip in audioTrack.Clips)
                {
                    float x1 = ValueToCanvasX(clip.StartTime);
                    float x2 = ValueToCanvasX(clip.StartTime + clip.Duration);
                    canvas.RoundedRectBorder(x1, trackRect.Min.Y, x2 - x1, trackRect.Size.Y, radius: EditorTheme.Roundness, thickness: 2, color: EditorTheme.Accent);

                    // Clip the text.
                    canvas.Scissor(x1 + 2, trackRect.Min.Y, x2 - x1 - 4, trackRect.Size.Y);

                    // Draw the waveform.
                    var waveform = AudioClipAssetEditor.GetWaveform(clip.Audio);
                    canvas.SetStrokeColor(EditorTheme.Ink300);
                    canvas.MoveTo(x1, trackRect.Center.Y);
                    for (int i = 0; i < waveform.Length; i ++)
                        canvas.LineTo(x: x1 + (float)i / waveform.Length * (x2 - x1),
                            y: trackRect.Center.Y - waveform[i] * (rowHeight / 2));
                    canvas.LineTo(x2, trackRect.Center.Y);
                    canvas.Stroke();

                    // Draw the clip name.
                    var textLayoutSettings = TextLayoutSettings.Default with
                    {
                        Font = font,
                        //Alignment = Scribe.TextAlignment.Center,
                        PixelSize = 16,
                        MaxWidth = x2 - x1,
                        
                    };
                    canvas.DrawText(clip.Audio.ClipName, x1 + 5, y + rowHeight / 2 - tickMarkLabelFontSize / 2 - 2, EditorTheme.AccentText, textLayoutSettings);
                }
                canvas.ResetScissor();

                // If the user is dragging an audio clip over an audio track, show a position indicator.
                
                if (IsCompatibleDragTarget(typeof(AudioClip)) && hover)
                {
                    canvas.RectFilled(paper.PointerPos.X - 1, trackRect.Min.Y, width: 2, rowHeight, color: EditorTheme.Accent);
                }

                // If the user dropped an audio clip over an audio track, add the clip.
                AssetDragPayload? drop = DragDrop.AcceptDrop<AssetDragPayload>(isHovered: hover,
                        dp => dp.AssetType != null && typeof(AudioClip).IsAssignableFrom(dp.AssetType));
                if (drop is not null)
                {
                    var audio = (AudioClip)AssetDatabase.Get(drop.AssetGuid);
                    audioTrack.AddClip(new TimelineAudioClip
                    {
                        Audio = audio,
                        StartTime = CanvasXToValue(paper.PointerPos.X),
                        Duration = audio.LengthInSeconds,
                    });
                }
            }

            // Show a context menu.

            //using (paper.Row($"tl_track_{index}")
            //    .Height(rowHeight)
            //    .BackgroundColor(EditorTheme.Glass)
            //    .Margin(left: 0, right: 0, top: 2, bottom: 2)
            //    .Enter())
            //{
            //    paper.Box($"tl_track_name_{index}")
            //        .PaddingLeft(10)
            //        .Alignment(PaperUI.TextAlignment.MiddleLeft)
            //        .Text(track.GetType().Name, font)
            //        .TextColor(EditorTheme.Ink400)
            //        .FontSize(EditorTheme.FontSize);
            //}
            index++;
        }

        // Draw play head.
        float headTop = elementBounds.Min.Y + 1;
        float headBottom = elementBounds.Min.Y + headerHeight;
        float headX = ValueToCanvasX(_timeline.PlaybackOffset);
        canvas.BeginPath();
        canvas.SetFillColor(EditorTheme.Ink700);
        canvas.MoveTo(headX, headBottom);
        canvas.LineTo(headX - 6, headBottom - 6);
        canvas.LineTo(headX - 6, headTop);
        canvas.LineTo(headX + 6, headTop);
        canvas.LineTo(headX + 6, headBottom - 6);
        canvas.Fill();

    }
    private void ShowAddTrackMenu(Paper paper)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            menu.Item(Loc.Get("timeline.add_audio_track"), AddAudioTrack);
        });
    }

    private void AddAudioTrack()
    {
        if (_timeline.IsNotValid())
            return;
        var track = new TimelineAudioTrack();
        _timeline.Tracks.Add(track);
        Selection.Select(track);
    }

    private void ShowTrackContextMenu(Paper paper, Timeline timeline, TimelineTrack track)
    {
        Selection.Select(track);
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu =>
        {
            menu.Item(Loc.Get("timeline.track_duplicate"), onClick: () => timeline.DuplicateTrack(track), icon: EditorIcons.Clone);
            menu.Item(Loc.Get("timeline.track_delete"), onClick: () =>
            {
                Undo.RegisterAction("Delete Track", undo: () => timeline.Tracks.Add(track), () => timeline.DeleteTrack(track));
                timeline.DeleteTrack(track);
                Selection.Clear();
            }, danger: true, icon: EditorIcons.Trash);
            menu.Separator();
            if (track.IsReadOnly)
                menu.Item(Loc.Get("timeline.track_unlock"), onClick: () => track.IsReadOnly = false, icon: EditorIcons.Unlock);
            else
                menu.Item(Loc.Get("timeline.track_lock"), onClick: () => track.IsReadOnly = true, icon: EditorIcons.Lock);
            if (track.Enabled)
                menu.Item(Loc.Get("timeline.track_disable"), onClick: () => track.Enabled = false, icon: EditorIcons.ToggleOff);
            else
                menu.Item(Loc.Get("timeline.track_enable"), onClick: () => track.Enabled = true, icon: EditorIcons.ToggleOn);
        });
    }
}
