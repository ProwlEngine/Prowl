// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Runtime.Audio;
using Prowl.Vector;

using Color = System.Drawing.Color;
using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for .audiomixer assets: a console of channel strips, one per bus, and the effects of
/// whichever is selected.
/// </summary>
/// <remarks>
/// Laid out the way a mixing desk is rather than as a tree of foldouts, because the question being
/// asked of it is how these levels sit against each other, which is a comparison across buses. The
/// strips carry the parts of that answer: a fader, what the bus is actually putting out, and whether
/// something is holding it quiet.
///
/// The mixer is edited live, so a level change is audible while play mode runs. Writing to disk is
/// separate, and the base class works out whether there is anything to write by diffing against the
/// imported form.
/// </remarks>
[CustomAssetEditor(typeof(AudioMixer))]
public class AudioMixerAssetEditor : AssetImporterEditor
{
    private const float StripWidth = 96.0f;
    private const float FaderHeight = 190.0f;
    private const float MeterWidth = 14.0f;

    /// <summary>Loudest and quietest a meter shows, in decibels. Below the floor reads as nothing.</summary>
    private const float MeterCeilingDB = 6.0f;
    private const float MeterFloorDB = -60.0f;

    /// <summary>Which bus the panel under the console is showing, by identity rather than position.</summary>
    private string _selected = string.Empty;

    // The inspector does not say how wide it is, so a full width box measures it and the console is
    // built to that on the next frame. One frame of lag while a panel is being dragged wider is not
    // something anyone can see.
    private float _consoleWidth = 320.0f;

    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => asset is AudioMixer mixer && mixer.IsValid() ? SerializePersisted(mixer) : null;

    /// <summary>
    /// Baselines against what was imported rather than the live object, so a mixer changed from a
    /// script shows up as pending instead of being adopted as already saved.
    /// </summary>
    protected override EchoObject? CapturePersistedState(AssetEntry entry, EngineObject? asset)
        => EditorAssetBackend.Instance?.ReadCachedEcho(entry.Guid) ?? CaptureState(entry, asset);

    /// <summary>Serializes the mixer the way its file is written: AssetID cleared, so the whole object
    /// is emitted rather than an $assetId reference back to itself.</summary>
    private static EchoObject SerializePersisted(AudioMixer mixer)
    {
        Guid savedId = mixer.AssetID;
        mixer.AssetID = Guid.Empty;
        try { return Serializer.Serialize(typeof(object), mixer); }
        finally { mixer.AssetID = savedId; }
    }

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
    {
        if (asset is not AudioMixer mixer || mixer.IsNotValid()) return false;
        if (Project.Current == null) return false;

        try
        {
            string absolute = Path.Combine(Project.Current.AssetsPath, entry.Path);
            File.WriteAllText(absolute, SerializePersisted(mixer).WriteToString());
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to save audio mixer '{entry.Path}': {ex.Message}");
            return false;
        }

        EditorAssetBackend.Instance?.Reimport(entry.Guid);
        return true;
    }

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        if (asset is not AudioMixer mixer || mixer.IsNotValid()) return;

        // Restored onto the live instance, so every source already pointing at one of its groups keeps
        // pointing at the same object and hears the revert immediately.
        mixer.ReleaseNative();
        Serializer.DeserializeInto(baseline, mixer);
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        Origami.Header(paper, $"{id}_h_info", $"{EditorIcons.Sliders}  Audio Mixer").Show();
        Origami.Label(paper, $"{id}_path", $"Path: {entry.Path}").Show();

        if (asset is not AudioMixer mixer || mixer.IsNotValid()) return;

        // Measuring what each bus puts out is work on the audio thread, so it only happens while
        // something is drawing meters. Asked for again every frame, and it lapses on its own.
        AudioMixerGroup.RequestMetering();

        List<Strip> strips = BuildStrips(mixer);

        paper.Box($"{id}_probe").Width(UnitValue.Stretch()).Height(0).IsNotInteractable()
            .OnPostLayout((_, rect) => _consoleWidth = (float)rect.Size.X);

        DrawToolbar(paper, id, mixer);
        DrawSnapshots(paper, id, mixer);
        DrawConsole(paper, id, mixer, strips);
        DrawSelected(paper, id, mixer);

        DrawApplyRevertBar(paper, id, entry, asset);
    }

    /// <summary>A bus and how deep it sits, in the order the strips are laid out.</summary>
    private readonly struct Strip(AudioMixerGroup group, int depth)
    {
        public readonly AudioMixerGroup Group = group;
        public readonly int Depth = depth;
    }

    /// <summary>
    /// Every bus, depth first from the master, so a child stands next to what it feeds.
    /// </summary>
    /// <remarks>
    /// The children of each bus are gathered in one pass rather than by rescanning the whole mixer at
    /// every level, which is what the tree of foldouts this replaced did.
    /// </remarks>
    private static List<Strip> BuildStrips(AudioMixer mixer)
    {
        var children = new Dictionary<AudioMixerGroup, List<AudioMixerGroup>>();
        var strips = new List<Strip>();

        foreach (AudioMixerGroup group in mixer.Groups)
        {
            if (group.IsNotValid()) continue;

            AudioMixerGroup parent = group.Parent;

            if (parent.IsNotValid()) continue;

            if (!children.TryGetValue(parent, out List<AudioMixerGroup> list))
                children[parent] = list = [];

            list.Add(group);
        }

        AudioMixerGroup master = mixer.Master;

        if (master.IsValid())
            Walk(master, 0);

        return strips;

        void Walk(AudioMixerGroup group, int depth)
        {
            strips.Add(new Strip(group, depth));

            if (!children.TryGetValue(group, out List<AudioMixerGroup> list)) return;

            foreach (AudioMixerGroup child in list)
                Walk(child, depth + 1);
        }
    }

    private void DrawToolbar(Paper paper, string id, AudioMixer mixer)
    {
        var m = Origami.Current.Metrics;

        using (paper.Row($"{id}_toolbar").Height(28).ColBetween(m.SpacingMedium)
            .Margin(0, 0, m.Spacing, m.Spacing).Enter())
        {
            Origami.Button(paper, $"{id}_add", "Add Group", () =>
            {
                // Added under whatever is selected, so building a tree is a matter of picking the bus
                // to nest under first. IsValid rather than a null check: a destroyed group is not a
                // parent either, and the selection can outlive a removal.
                AudioMixerGroup selected = Find(mixer, _selected);
                AudioMixerGroup parent = selected.IsValid() ? selected : mixer.Master;
                AudioMixerGroup added = mixer.AddGroup(UniqueGroupName(mixer, "New Group"), parent);
                _selected = added.Identity;
            }).LeadingIcon(EditorIcons.Plus).Small().Show();

            if (mixer.AnySolo)
            {
                Origami.Button(paper, $"{id}_clearsolo", "Clear Solo", mixer.ClearSolo)
                    .LeadingIcon(EditorIcons.Headphones).Small().Warning().Show();
            }

            // Pushes what follows to the right.
            paper.Box($"{id}_toolbar_gap").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            paper.Box($"{id}_count").Width(UnitValue.Auto).Height(20)
                .Text($"{mixer.Groups.Count} buses", EditorTheme.DefaultFont)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleRight).IsNotInteractable();
        }
    }

    /// <summary>Which snapshot the row below the toolbar is editing, by name.</summary>
    private string _snapshot = string.Empty;

    /// <summary>How long a click on a snapshot takes to move the mixer there.</summary>
    private float _transitionSeconds = 0.5f;

    /// <summary>
    /// The recorded mixes, and the controls for taking and editing them.
    /// </summary>
    /// <remarks>
    /// Clicking one moves the mixer to it over the transition time rather than snapping, because the
    /// thing being authored is the move: how long a pause menu takes to duck the music is as much a
    /// part of it as how far it ducks.
    /// </remarks>
    private void DrawSnapshots(Paper paper, string id, AudioMixer mixer)
    {
        var m = Origami.Current.Metrics;
        AudioMixerSnapshot selected = mixer.FindSnapshot(_snapshot);

        using (paper.Row($"{id}_snaps").Height(26).ColBetween(m.SpacingSmall)
            .Margin(0, 0, 0, m.Spacing).Enter())
        {
            paper.Box($"{id}_snaps_lbl").Width(70).Height(22)
                .Text("Snapshots", EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            IReadOnlyList<AudioMixerSnapshot> snapshots = mixer.Snapshots;

            for (int i = 0; i < snapshots.Count; i++)
            {
                AudioMixerSnapshot snapshot = snapshots[i];

                if (snapshot == null) continue;

                bool active = ReferenceEquals(snapshot, mixer.ActiveSnapshot);
                float seconds = _transitionSeconds;

                Origami.Button(paper, $"{id}_snap{i}", snapshot.Name, () =>
                {
                    _snapshot = snapshot.Name;
                    mixer.TransitionTo(snapshot, seconds);
                }).Small().Style(active ? ButtonStyle.Filled : ButtonStyle.Outline)
                  .Tooltip($"Move the mixer to '{snapshot.Name}' over {seconds:0.##}s").Show();
            }

            Origami.Button(paper, $"{id}_snap_add", "Capture", () =>
            {
                AudioMixerSnapshot taken = mixer.CaptureSnapshot(UniqueSnapshotName(mixer, "Snapshot"));
                _snapshot = taken.Name;
            }).LeadingIcon(EditorIcons.Plus).Small()
              .Tooltip("Record the mixer as it is now as a new snapshot").Show();

            paper.Box($"{id}_snaps_gap").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            paper.Box($"{id}_snap_time_lbl").Width(UnitValue.Auto).Height(22)
                .Text("Transition", EditorTheme.DefaultFont).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight)
                .IsNotInteractable();

            Origami.Slider(paper, $"{id}_snap_time", _transitionSeconds, v => _transitionSeconds = v, 0.0f, 5.0f)
                .Width(120).Format("0.##s").Small().Show();
        }

        if (selected == null) return;

        // Editing one is a second row rather than a popup: the thing being edited is the mixer behind
        // it, and a dialog over the console would hide what a recapture is about to record.
        using (paper.Row($"{id}_snap_edit").Height(26).ColBetween(m.SpacingSmall)
            .Margin(0, 0, 0, m.Spacing).Enter())
        {
            AudioMixerSnapshot editing = selected;

            Origami.TextField(paper, $"{id}_snap_name", editing.Name, v =>
            {
                editing.Name = v;
                _snapshot = editing.Name;
            }).Width(180).Show();

            Origami.Button(paper, $"{id}_snap_recapture", "Recapture", () => editing.CaptureFrom(mixer))
                .Small().Style(ButtonStyle.Outline)
                .Tooltip("Replace what this snapshot holds with the mixer as it is now").Show();

            Origami.Button(paper, $"{id}_snap_apply", "Apply", () => mixer.ApplySnapshot(editing))
                .Small().Style(ButtonStyle.Outline)
                .Tooltip("Put the mixer into this snapshot at once, with no transition").Show();

            paper.Box($"{id}_snap_edit_gap").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            Origami.Button(paper, $"{id}_snap_remove", "Delete", () =>
            {
                mixer.RemoveSnapshot(editing);
                _snapshot = string.Empty;
            }).LeadingIcon(EditorIcons.Trash).Small().Danger().Style(ButtonStyle.Outline).Show();
        }
    }

    private static string UniqueSnapshotName(AudioMixer mixer, string baseName)
    {
        if (mixer.FindSnapshot(baseName) == null) return baseName;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{baseName} {i}";
            if (mixer.FindSnapshot(candidate) == null) return candidate;
        }

        return baseName;
    }

    private void DrawConsole(Paper paper, string id, AudioMixer mixer, List<Strip> strips)
    {
        var m = Origami.Current.Metrics;

        // Scrolls sideways rather than wrapping, because a desk is a row: strips only mean anything
        // next to each other.
        Origami.ScrollView(paper, $"{id}_console", Maths.Max(240.0f, _consoleWidth), 340)
            .Vertical(false).Horizontal(true).Padding(m.Spacing)
            .Body(() =>
            {
                using (paper.Row($"{id}_strips").Height(UnitValue.Stretch()).ColBetween(m.SpacingSmall).Enter())
                {
                    for (int i = 0; i < strips.Count; i++)
                        DrawStrip(paper, id, mixer, strips[i], i);
                }
            });
    }

    private void DrawStrip(Paper paper, string id, AudioMixer mixer, Strip strip, int index)
    {
        AudioMixerGroup group = strip.Group;
        var m = Origami.Current.Metrics;

        string stripId = $"{id}_s{index}";
        bool selected = group.Identity == _selected;
        bool master = ReferenceEquals(group, mixer.Master);

        using (paper.Column(stripId).Width(StripWidth).Height(UnitValue.Stretch())
            .Rounded(m.ContainerRounding).Padding(m.SpacingSmall).RowBetween(m.SpacingSmall)
            .BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Glass)
            .BorderColor(selected ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1)
            .OnClick(group.Identity, (identity, _) => _selected = identity)
            .Enter())
        {
            // Depth is spelled rather than indented: strips have to stay shoulder to shoulder to be
            // compared, so the hierarchy is carried by the label and by the routing at the bottom.
            string prefix = strip.Depth == 0 ? string.Empty : new string('>', strip.Depth) + " ";

            paper.Box($"{stripId}_name").Height(18)
                .Text($"{prefix}{group.GroupName}", EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont)
                .TextColor(selected ? EditorTheme.Ink600 : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .TextTruncate().Tooltip(group.GroupName);

            DrawFaderRow(paper, stripId, group);

            paper.Box($"{stripId}_db").Height(16)
                .Text(FormatDecibels(group.VolumeDB), EditorTheme.FontMono ?? EditorTheme.DefaultFont)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter).IsNotInteractable();

            DrawMuteSolo(paper, stripId, group);
            DrawRouting(paper, stripId, mixer, group, master);
        }
    }

    /// <summary>The meter and the fader, side by side, which is the pairing the panel exists for.</summary>
    private static void DrawFaderRow(Paper paper, string stripId, AudioMixerGroup group)
    {
        using (paper.Row($"{stripId}_fader_row").Height(FaderHeight).ColBetween(6)
            .ChildLeft().ChildRight().Enter())
        {
            float peak = group.PeakLevel;

            // Instant on the way up, eased on the way down. A meter that rose as slowly as it falls
            // would miss every transient, which is most of what there is to see.
            float eased = paper.AnimateFloat(peak, 5.0f, id: stripId);
            float shown = Maths.Max(peak, eased);
            bool silenced = group.Mute || group.SilencedBySolo;

            paper.Box($"{stripId}_meter").Width(MeterWidth).Height(UnitValue.Stretch())
                .Rounded(3).IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    DrawMeter(canvas, r, shown, silenced)));

            Origami.Slider(paper, $"{stripId}_fader", group.VolumeDB, v => group.VolumeDB = v,
                    AudioMixerGroup.MinVolumeDB, AudioMixerGroup.MaxVolumeDB)
                .Vertical().Height(FaderHeight).ShowValue(false).Show();
        }
    }

    private static void DrawMuteSolo(Paper paper, string stripId, AudioMixerGroup group)
    {
        AudioMixerGroup captured = group;

        using (paper.Row($"{stripId}_ms").Height(22).ColBetween(4).Enter())
        {
            Origami.Button(paper, $"{stripId}_mute", "M", () => captured.Mute = !captured.Mute)
                .Width(UnitValue.Stretch()).Height(22)
                .Style(group.Mute ? ButtonStyle.Filled : ButtonStyle.Outline)
                .Danger().Tooltip("Silence this bus and everything feeding it").Show();

            Origami.Button(paper, $"{stripId}_solo", "S", () => captured.Solo = !captured.Solo)
                .Width(UnitValue.Stretch()).Height(22)
                .Style(group.Solo ? ButtonStyle.Filled : ButtonStyle.Outline)
                .Warning().Tooltip("Hear this bus on its own").Show();
        }
    }

    /// <summary>Which bus this one feeds. The master has nowhere else to go, so it says so instead.</summary>
    private static void DrawRouting(Paper paper, string stripId, AudioMixer mixer, AudioMixerGroup group, bool master)
    {
        if (master)
        {
            paper.Box($"{stripId}_out").Height(22)
                .Text("to output", EditorTheme.DefaultFont).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .IsNotInteractable();
            return;
        }

        // Anything this bus already feeds would be a loop, so it is not offered as somewhere to go.
        var options = new List<AudioMixerGroup>();

        foreach (AudioMixerGroup candidate in mixer.Groups)
        {
            if (candidate.IsNotValid() || ReferenceEquals(candidate, group)) continue;
            if (group.IsAncestorOf(candidate)) continue;

            options.Add(candidate);
        }

        AudioMixerGroup captured = group;

        Origami.Dropdown(paper, $"{stripId}_out", group.Parent, p => mixer.SetParent(captured, p), options)
            .Display(g => g.IsValid() ? g.GroupName : "Output")
            .Placeholder("Output")
            .Show();
    }

    /// <summary>
    /// A vertical bar of what the bus is putting out, on a decibel scale so the quiet end is readable.
    /// </summary>
    /// <remarks>
    /// Green up to amber and then red, with the red starting where a mix is close enough to full scale
    /// that the next transient clips. A silenced bus draws as an empty track rather than staying at
    /// whatever it last showed, because it is producing nothing.
    /// </remarks>
    private static void DrawMeter(Canvas canvas, Rect rect, float peak, bool silenced)
    {
        float x = (float)rect.Min.X;
        float y = (float)rect.Min.Y;
        float w = (float)rect.Size.X;
        float h = (float)rect.Size.Y;

        canvas.RoundedRectFilled(x, y, w, h, 3, ToCanvas(EditorTheme.Neutral200));

        if (silenced || peak <= 0.0f) return;

        float decibels = 20.0f * MathF.Log10(peak);
        float filled = Maths.Clamp((decibels - MeterFloorDB) / (MeterCeilingDB - MeterFloorDB), 0.0f, 1.0f);

        if (filled <= 0.0f) return;

        float barHeight = h * filled;

        Color32 low = ToCanvas(EditorTheme.Green400);
        Color32 high = decibels > -3.0f ? ToCanvas(EditorTheme.Red400) : ToCanvas(EditorTheme.Amber400);

        canvas.SetLinearBrush(x, y + h, x, y, low, high);
        canvas.RoundedRectFilled(x, y + h - barHeight, w, barHeight, 3, Color32.FromArgb(255, 255, 255, 255));
        canvas.ClearBrush();
    }

    private void DrawSelected(Paper paper, string id, AudioMixer mixer)
    {
        AudioMixerGroup group = Find(mixer, _selected);

        if (group.IsNotValid())
        {
            // Nothing chosen yet, or what was chosen has gone. The master is always there.
            group = mixer.Master;

            if (group.IsNotValid()) return;

            _selected = group.Identity;
        }

        var m = Origami.Current.Metrics;
        bool master = ReferenceEquals(group, mixer.Master);
        AudioMixerGroup captured = group;

        Origami.Separator(paper, $"{id}_sep_sel").Show();

        using (paper.Row($"{id}_sel_hdr").Height(30).ColBetween(m.SpacingMedium).Enter())
        {
            Origami.Header(paper, $"{id}_sel_h", $"{EditorIcons.WaveSquare}  {group.GroupName}").Show();

            paper.Box($"{id}_sel_gap").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            if (!master)
            {
                Origami.Button(paper, $"{id}_sel_remove", "Remove", () =>
                {
                    mixer.RemoveGroup(captured);
                    _selected = string.Empty;
                }).LeadingIcon(EditorIcons.Trash).Small().Danger().Style(ButtonStyle.Outline).Show();
            }
        }

        // The strip already carries level, mute and routing, so what is left for here is the bus
        // itself: its name, and the effects everything feeding it runs through.
        PropertyGridUtils.Draw(paper, $"{id}_sel_props", group, _ => captured.OnValidate());
    }

    private static AudioMixerGroup Find(AudioMixer mixer, string identity)
    {
        if (string.IsNullOrEmpty(identity)) return null;

        foreach (AudioMixerGroup group in mixer.Groups)
            if (group.IsValid() && group.Identity == identity) return group;

        return null;
    }

    private static string FormatDecibels(float decibels)
        => decibels <= AudioMixerGroup.MinVolumeDB ? "-inf" : $"{decibels:+0.0;-0.0;0.0} dB";

    private static Color32 ToCanvas(Color color) => Color32.FromArgb(color.A, color.R, color.G, color.B);

    private static string UniqueGroupName(AudioMixer mixer, string baseName)
    {
        if (mixer.FindGroup(baseName).IsNotValid()) return baseName;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{baseName} {i}";
            if (mixer.FindGroup(candidate).IsNotValid()) return candidate;
        }

        return baseName;
    }
}
