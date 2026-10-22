using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(InputActionMap))]
public class InputActionMapEditor : AssetImporterEditor
{
    private string? _openAction;
    private string _filter = "";
    private string? _confirmDelete;
    private readonly InputBindingPopup _popup = new();

    // ============================================================
    // Pending changes
    // ============================================================
    // The map is edited live, so anything already bound to it reacts as you go. The base class measures
    // whether that live object still matches the file on disk.

    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => asset is InputActionMap map && map.IsValid() ? Serialize(map) : null;

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
        => asset is InputActionMap map && map.IsValid() && SaveMap(map, entry);

    /// <summary>
    /// Baselines against the imported form rather than the live object, so a map mutated outside this
    /// inspector shows up as pending instead of being quietly adopted as the clean state.
    /// </summary>
    protected override EchoObject? CapturePersistedState(AssetEntry entry, EngineObject? asset)
        => EditorAssetBackend.Instance?.ReadCachedEcho(entry.Guid) ?? CaptureState(entry, asset);

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        if (asset is not InputActionMap map || map.IsNotValid()) return;

        // Refilled in place, so anything already holding this map sees the revert.
        _popup.Close();
        EditorAssetBackend.Instance?.RevertToSaved(map);
    }

    /// <summary>Serializes the map as its file form.</summary>
    private static EchoObject Serialize(InputActionMap map) => Serializer.Serialize(typeof(object), map);

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null || asset is not InputActionMap map) return;

        if (_popup.IsOpen && _popup.Map != map) _popup.Close();
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row($"{id}_tools").Height(34).Gap(m.SpacingLarge)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Enter())
        {
            using (paper.Box($"{id}_search_w").Width(UnitValue.Stretch()).Height(34).Enter())
                Origami.SearchField(paper, $"{id}_search", _filter, v => _filter = v, "Search actions or keys").Height(34).Show();

            paper.Box($"{id}_add").Width(UnitValue.Auto).Height(34).Padding(14, 14, 0, 0).Rounded(m.Rounding)
                .BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text($"{EditorIcons.Plus}  Action", EditorTheme.FontSemiBold ?? font).TextColor(Color.White)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .Cursor(PaperCursor.Pointer)
                .OnClick(0, (_, _) => AddAction(map));
        }

        Dictionary<string, List<string>> usage = BuildUsage(map);
        List<InputAction> shown = [.. map.Actions.Where(Matches)];

        using (paper.Column($"{id}_list").Height(UnitValue.Auto).Gap(4)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.PaddingLarge).Enter())
        {
            if (shown.Count == 0)
                EditorGUI.EmptyState(paper, $"{id}_empty",
                    map.Actions.Count == 0 ? "No actions yet. Add one to get started." : $"No action or key matches \"{_filter}\"", font);

            foreach (InputAction action in shown)
                DrawAction(paper, $"{id}_a{map.IndexOf(action.Name)}", map, action, usage);
        }

        DrawPendingFooter(paper, $"{id}_pending", entry, asset);
    }

    public override void OnPanelOverlay(Paper paper, string id, float width, float height)
    {
        if (_popup.IsOpen) _popup.Draw(paper, id, width, height);
    }

    /// <summary>A bar under the list that says the map has unsaved edits, with Revert and Apply.</summary>
    private void DrawPendingFooter(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (Origami.IsReadOnly || !HasPendingChanges(entry, asset)) return;
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row(id).Height(48).Padding(14, 10, 0, 0).Gap(m.SpacingLarge)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.SpacingLarge, m.SpacingLarge)
            .Rounded(m.ContainerRounding).BackgroundColor(EditorTheme.Neutral400)
            .BorderColor(EditorTheme.WithAlpha(EditorTheme.Amber400, 90)).BorderWidth(1).Enter())
        {
            paper.Box($"{id}_dot").Width(8).Height(8).Rounded(4).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .BackgroundColor(EditorTheme.Amber400).IsNotInteractable();
            paper.Box($"{id}_t").Height(48)
                .Text("Unsaved changes, already live in play mode", EditorTheme.FontMedium ?? EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Amber400).FontSize(InputBindingDisplay.LabelSize + 1)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
            InputBindingDisplay.SmallButton(paper, $"{id}_revert", Prowl.Rosetta.Loc.Get("dialog.revert"), () => RevertPendingChanges(entry, asset));
            InputBindingDisplay.SmallButton(paper, $"{id}_apply", $"{EditorIcons.FloppyDisk}  {Prowl.Rosetta.Loc.Get("dialog.apply")}",
                () => ApplyPendingChanges(entry, asset), primary: true);
        }
    }

    private bool Matches(InputAction action)
    {
        if (string.IsNullOrWhiteSpace(_filter)) return true;
        return action.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || Controls(action).Any(c => c.Label.Contains(_filter, StringComparison.OrdinalIgnoreCase));
    }

    private void AddAction(InputActionMap map)
    {
        string name = FindUniqueName(map, "NewAction");
        map.AddAction(name, InputActionType.Button);
        _openAction = name;
        _filter = "";
    }

    // ================================================================
    //  Action row
    // ================================================================

    private void DrawAction(Paper paper, string id, InputActionMap map, InputAction action, Dictionary<string, List<string>> usage)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        Scribe.FontFile font = EditorTheme.DefaultFont!;
        bool open = _openAction == action.Name;
        ActionKind kind = InputBindingDisplay.GetKind(action);
        List<string> shared = SharedWith(action, usage);
        Color kindColor = InputBindingDisplay.KindColor(kind);

        const float rowHeight = 42f;
        using IDisposable group = paper.Column($"{id}_group").Height(UnitValue.Auto)
            .Rounded(m.ContainerRounding)
            .BackgroundColor(open ? EditorTheme.Neutral400 : Color.Transparent)
            .BorderColor(open ? EditorTheme.BorderStrong : Color.Transparent).BorderWidth(1)
            .Margin(0, 0, 0, open ? m.SpacingLarge : 0)
            .Enter();

        using (paper.Row($"{id}_row").Height(rowHeight).Padding(12, 12, 0, 0).Gap(m.SpacingLarge + 2).Clip()
            .Rounded(m.ContainerRounding)
            .BackgroundColor(Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Ink300, open ? 10 : 18)).End()
            .Cursor(PaperCursor.Pointer)
            .OnClick(action.Name, (n, _) =>
            {
                _openAction = _openAction == n ? null : n;
                _confirmDelete = null;
                _popup.Close();
            })
            .Enter())
        {
            paper.Box($"{id}_kind").Width(26).Height(26).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Rounded(m.SmallRounding + 1).BackgroundColor(EditorTheme.WithAlpha(kindColor, 36))
                .Text(InputBindingDisplay.KindIcon(kind), font).TextColor(kindColor)
                .FontSize(InputBindingDisplay.LabelSize).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();

            paper.Box($"{id}_name").Width(UnitValue.Auto).Height(rowHeight)
                .Text(action.Name, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            if (action.Bindings.Count + action.CompositeBindings.Count == 0)
                InputBindingDisplay.Tag(paper, $"{id}_flag", "No bindings", EditorTheme.Amber400).IsNotInteractable();
            else if (shared.Count > 0)
                InputBindingDisplay.Tag(paper, $"{id}_flag", "Shared key", EditorTheme.Red400).Tooltip(string.Join("\n", shared));

            paper.Box($"{id}_sp").Height(1).IsNotInteractable();

            if (!open)
            {
                List<(string Label, InputDevice Device)> controls = Summaries(action);
                for (int i = 0; i < Math.Min(3, controls.Count); i++)
                    InputBindingDisplay.Keycap(paper, $"{id}_chip{i}", controls[i].Label, InputBindingDisplay.DeviceColor(controls[i].Device));
                if (controls.Count > 3)
                    InputBindingDisplay.Tag(paper, $"{id}_more", $"+{controls.Count - 3}", EditorTheme.Ink400).IsNotInteractable();
            }

            paper.Box($"{id}_chev").Width(20).Height(rowHeight)
                .Text(open ? EditorIcons.ChevronDown : EditorIcons.ChevronRight, font).TextColor(EditorTheme.Ink400)
                .FontSize(InputBindingDisplay.LabelSize).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
        }

        if (open)
        {
            EditorGUI.Divider(paper, $"{id}_div");
            DrawActionBody(paper, $"{id}_body", map, action, shared);
        }
    }

    // ================================================================
    //  Action body
    // ================================================================

    private void DrawActionBody(Paper paper, string id, InputActionMap map, InputAction action, List<string> shared)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        ActionKind kind = InputBindingDisplay.GetKind(action);

        using (paper.Column(id).Height(UnitValue.Auto).Gap(m.SpacingLarge)
            .Padding(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.PaddingLarge).Enter())
        {
            InputBindingDisplay.FieldHeading(paper, $"{id}_name_h", "Name");
            Origami.TextField(paper, $"{id}_name_v", action.Name, v =>
            {
                string oldName = action.Name;
                if (map.RenameAction(oldName, v.Trim()) && _openAction == oldName)
                    _openAction = action.Name;
            }).Height(34).CommitOnEnter().Show();

            InputBindingDisplay.FieldHeading(paper, $"{id}_kind_h", "Kind", "what your code reads");
            InputBindingDisplay.Segmented(paper, $"{id}_kind",
                [
                    new("Button", "on or off", InputBindingDisplay.KindDescription(ActionKind.Button)),
                    new("Axis", "float", InputBindingDisplay.KindDescription(ActionKind.Axis)),
                    new("Vector2", "Float2", InputBindingDisplay.KindDescription(ActionKind.Vector2)),
                    new("Pass Through", "raw", InputBindingDisplay.KindDescription(ActionKind.PassThrough)),
                ],
                Array.IndexOf(InputBindingDisplay.Kinds, kind), i => InputBindingDisplay.SetKind(action, InputBindingDisplay.Kinds[i]));

            if (kind == ActionKind.PassThrough)
                InputBindingDisplay.Segmented(paper, $"{id}_out", [new("float"), new("Float2")],
                    InputBindingDisplay.OutputsVector(action) ? 1 : 0,
                    i => action.ExpectedValueType = i == 1 ? typeof(Prowl.Vector.Float2) : typeof(float));

            InputBindingDisplay.Hint(paper, $"{id}_kind_hint", InputBindingDisplay.KindDescription(kind));

            int count = action.Bindings.Count + action.CompositeBindings.Count;
            InputBindingDisplay.FieldHeading(paper, $"{id}_b_h", "Bindings", count == 1 ? "1 binding" : $"{count} bindings");

            using (paper.Column($"{id}_blist").Height(UnitValue.Auto).Gap(6).Enter())
            {
                for (int i = 0; i < action.CompositeBindings.Count; i++)
                    DrawCompositeRow(paper, $"{id}_c{i}", map, action, action.CompositeBindings[i]);
                for (int i = 0; i < action.Bindings.Count; i++)
                    DrawBindingRow(paper, $"{id}_b{i}", map, action, action.Bindings[i]);

                bool adding = _popup.IsOpenFor(action, null);
                paper.Box($"{id}_addb").Height(38).Rounded(m.Rounding)
                    .BackgroundColor(adding ? EditorTheme.WithAlpha(EditorTheme.Accent, 30) : Color.Transparent)
                    .BorderColor(adding ? EditorTheme.Accent : EditorTheme.BorderStrong).BorderWidth(1)
                    .Hovered.BorderColor(EditorTheme.AccentBright).BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Ink300, 14)).End()
                    .Text($"{EditorIcons.Plus}  Add binding", EditorTheme.FontMedium ?? EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink400)
                    .FontSize(InputBindingDisplay.LabelSize + 1).Alignment(TextAlignment.MiddleCenter)
                    .Cursor(PaperCursor.Pointer)
                    .OnClick(0, (_, _) => _popup.OpenAdd(map, action));
            }

            if (shared.Count > 0)
                InputBindingDisplay.Hint(paper, $"{id}_shared", string.Join(". ", shared) + ".", EditorTheme.Red400);

            DrawCodeLine(paper, $"{id}_code", action);
            DrawActionFooter(paper, $"{id}_foot", map, action);
        }
    }

    private void DrawCodeLine(Paper paper, string id, InputAction action)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        string code = InputBindingDisplay.CodeLine(action);

        using (paper.Row(id).Height(36).Padding(12, 5, 0, 0).Gap(6).Clip()
            .Rounded(m.Rounding).BackgroundColor(EditorTheme.Neutral200).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            paper.Box($"{id}_t").Height(36)
                .Text(code, EditorTheme.FontMono ?? EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink400)
                .FontSize(InputBindingDisplay.LabelSize).Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
            InputBindingDisplay.IconBox(paper, $"{id}_copy", EditorIcons.Copy, () => Input.Clipboard = code, tooltip: "Copy");
        }
    }

    private void DrawActionFooter(Paper paper, string id, InputActionMap map, InputAction action)
    {
        int index = map.IndexOf(action.Name);
        using (paper.Row(id).Height(30).Gap(6).Enter())
        {
            InputBindingDisplay.SmallButton(paper, $"{id}_up", $"{EditorIcons.ChevronUp}  Up", () => map.MoveAction(index, index - 1), disabled: index <= 0);
            InputBindingDisplay.SmallButton(paper, $"{id}_down", $"{EditorIcons.ChevronDown}  Down", () => map.MoveAction(index, index + 1),
                disabled: index >= map.Actions.Count - 1);
            InputBindingDisplay.SmallButton(paper, $"{id}_dup", $"{EditorIcons.Clone}  Duplicate", () => DuplicateAction(map, action));

            paper.Box($"{id}_sp").Height(1).IsNotInteractable();

            if (_confirmDelete == action.Name)
            {
                InputBindingDisplay.Caption(paper, $"{id}_q", $"Delete {action.Name}?");
                InputBindingDisplay.SmallButton(paper, $"{id}_yes", "Delete", () =>
                {
                    map.RemoveAction(action.Name);
                    _openAction = null;
                    _confirmDelete = null;
                }, EditorTheme.Red400);
                InputBindingDisplay.SmallButton(paper, $"{id}_no", "Keep", () => _confirmDelete = null);
            }
            else
            {
                InputBindingDisplay.SmallButton(paper, $"{id}_del", $"{EditorIcons.Trash}  Delete", () => _confirmDelete = action.Name, EditorTheme.Red400);
            }
        }
    }

    // ================================================================
    //  Binding rows
    // ================================================================

    private void DrawBindingRow(Paper paper, string id, InputActionMap map, InputAction action, InputBinding binding)
    {
        InputDevice device = InputBindingDisplay.GetDevice(binding);
        bool open = _popup.IsOpenFor(action, binding);
        using (BindingRow(paper, id, open, () => _popup.OpenEdit(map, action, binding)))
        {
            DeviceIcon(paper, $"{id}_dev", device);
            InputBindingDisplay.Keycap(paper, $"{id}_key", InputBindingDisplay.ControlName(binding), InputBindingDisplay.DeviceColor(device));
            paper.Box($"{id}_sp").Height(1).IsNotInteractable();

            DrawInteractionTags(paper, id, binding, InputBindingDisplay.IsAnalog(binding));
            if (device == InputDevice.Gamepad && binding.RequiredDeviceIndex.HasValue)
                InputBindingDisplay.Tag(paper, $"{id}_pad", $"Gamepad {binding.RequiredDeviceIndex}", EditorTheme.Ink400).IsNotInteractable();
            DrawProcessorTags(paper, id, binding.Processors);
            DrawMismatch(paper, id, InputBindingDisplay.Mismatch(action, InputBindingDisplay.GivesVector(binding), false));
            Chevron(paper, $"{id}_chev");
        }
    }

    private void DrawCompositeRow(Paper paper, string id, InputActionMap map, InputAction action, InputCompositeBinding composite)
    {
        bool open = _popup.IsOpenFor(action, composite);
        using (BindingRow(paper, id, open, () => _popup.OpenEdit(map, action, composite)))
        {
            DeviceIcon(paper, $"{id}_dev", InputBindingDisplay.GetDevice(composite));
            InputBindingDisplay.CompositeSummary(paper, $"{id}_sum", composite);
            paper.Box($"{id}_sp").Height(1).IsNotInteractable();

            DrawInteractionTags(paper, id, composite, analog: true);
            if (composite is Vector2CompositeBinding { Normalize: true })
                InputBindingDisplay.Tag(paper, $"{id}_norm", "Normalized", EditorTheme.Ink400).IsNotInteractable();
            DrawProcessorTags(paper, id, composite.Processors);
            DrawMismatch(paper, id, InputBindingDisplay.Mismatch(action, InputBindingDisplay.GivesVector(composite), true));
            Chevron(paper, $"{id}_chev");
        }
    }

    private static IDisposable BindingRow(Paper paper, string id, bool open, Action onClick)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        return paper.Row(id).Height(UnitValue.Auto).MinHeight(42).Padding(12, 10, 8, 8).Gap(m.SpacingLarge + 2)
            .Rounded(m.Rounding).BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(open ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(open ? EditorTheme.Accent : EditorTheme.BorderStrong).End()
            .Cursor(PaperCursor.Pointer)
            .OnClick(0, (_, _) => onClick())
            .Enter();
    }

    private static void DrawInteractionTags(Paper paper, string id, IInputInteractionSettings settings, bool analog)
    {
        if (settings.Interaction != InputInteractionType.Default)
            InputBindingDisplay.Tag(paper, $"{id}_it", InputBindingDisplay.InteractionSummary(settings), EditorTheme.AccentText).IsNotInteractable();
        if (analog && MathF.Abs(settings.PressPoint - 0.5f) > 0.001f)
            InputBindingDisplay.Tag(paper, $"{id}_pp", $"Press at {settings.PressPoint:0.##}", EditorTheme.Ink400).IsNotInteractable();
    }

    private static void DrawProcessorTags(Paper paper, string id, List<IInputProcessor> processors)
    {
        if (processors.Count > 0)
            InputBindingDisplay.Tag(paper, $"{id}_proc", processors.Count == 1 ? "1 processor" : $"{processors.Count} processors", EditorTheme.Ink400)
                .IsNotInteractable();
    }

    private static void DrawMismatch(Paper paper, string id, string? warning)
    {
        if (warning != null)
            InputBindingDisplay.Tag(paper, $"{id}_warn", warning, EditorTheme.Amber400).IsNotInteractable();
    }

    private static void DeviceIcon(Paper paper, string id, InputDevice device)
    {
        paper.Box(id).Width(18).Height(18).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Text(InputBindingDisplay.DeviceIcon(device), EditorTheme.DefaultFont!).TextColor(InputBindingDisplay.DeviceColor(device))
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
    }

    private static void Chevron(Paper paper, string id)
    {
        paper.Box(id).Width(18).Height(22).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Text(EditorIcons.ChevronRight, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink400)
            .FontSize(InputBindingDisplay.LabelSize - 1).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
    }

    // ================================================================
    //  Controls, summaries and shared keys
    // ================================================================

    /// <summary>Every control an action reads, with the interaction it is read through.</summary>
    private static IEnumerable<(string Label, string Key)> Controls(InputAction action)
    {
        foreach (InputCompositeBinding composite in action.CompositeBindings)
            foreach (InputBinding part in composite.Parts.Values)
                yield return (InputBindingDisplay.ControlName(part), $"{ControlKey(part)}|{composite.Interaction}");
        foreach (InputBinding binding in action.Bindings)
            yield return (InputBindingDisplay.ControlName(binding), $"{ControlKey(binding)}|{binding.Interaction}");
    }

    private static string ControlKey(InputBinding b)
        => $"{b.BindingType}:{b.Key}:{b.MouseButton}:{b.GamepadButton}:{b.AxisIndex}:{b.XRHand}:{b.XRButton}:{b.XRAxis}:{b.XRTouch}";

    private static Dictionary<string, List<string>> BuildUsage(InputActionMap map)
    {
        var usage = new Dictionary<string, List<string>>();
        foreach (InputAction action in map.Actions)
            foreach ((string _, string key) in Controls(action).DistinctBy(c => c.Key))
            {
                if (!usage.TryGetValue(key, out List<string>? names))
                    usage[key] = names = [];
                names.Add(action.Name);
            }
        return usage;
    }

    /// <summary>Lines like "E is also bound to Interact", for controls another action reads through the same interaction.</summary>
    private static List<string> SharedWith(InputAction action, Dictionary<string, List<string>> usage)
    {
        var lines = new List<string>();
        foreach ((string label, string key) in Controls(action).DistinctBy(c => c.Key))
        {
            if (key.StartsWith($"{InputBindingType.AnyKey}:") || key.StartsWith($"{InputBindingType.AnyGamepadButton}:")) continue;
            List<string> others = [.. usage[key].Where(n => n != action.Name)];
            if (others.Count > 0)
                lines.Add($"{label} is also bound to {string.Join(", ", others)}");
        }
        return lines;
    }

    private static List<(string Label, InputDevice Device)> Summaries(InputAction action)
    {
        var list = new List<(string, InputDevice)>();
        foreach (InputCompositeBinding composite in action.CompositeBindings)
            list.Add((string.Join(" ", composite.Parts.Values.Select(InputBindingDisplay.ControlName)), InputBindingDisplay.GetDevice(composite)));
        foreach (InputBinding binding in action.Bindings)
            list.Add((InputBindingDisplay.ControlName(binding), InputBindingDisplay.GetDevice(binding)));
        return list;
    }

    // ================================================================
    //  Helpers
    // ================================================================

    /// <summary>Copies an action through the file form, so every binding and processor is a fresh object.</summary>
    private void DuplicateAction(InputActionMap map, InputAction action)
    {
        var ctx = new SerializationContext();
        var tag = EchoObject.NewCompound();
        map.Serialize(ref tag, ctx);

        var scratch = new InputActionMap();
        scratch.Deserialize(tag, ctx);
        InputAction copy = scratch.GetAction(action.Name);
        scratch.RemoveAction(action.Name);

        copy.Name = FindUniqueName(map, action.Name + "Copy");
        map.InsertAction(map.IndexOf(action.Name) + 1, copy);
        _openAction = copy.Name;
    }

    private static bool SaveMap(InputActionMap map, AssetEntry entry)
        => EditorAssetBackend.Instance?.SaveAsset(entry.Guid, Serialize(map)) ?? false;

    private static string FindUniqueName(InputActionMap map, string baseName)
        => Utils.UniqueNames.MakeUnique(baseName, n => map.FindAction(n) != null,
            openSeparator: "", closeSeparator: "", stripExistingSuffix: false);
}
