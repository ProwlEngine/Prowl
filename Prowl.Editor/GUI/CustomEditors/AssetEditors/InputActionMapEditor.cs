using System;
using System.Collections.Generic;
using System.IO;
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
    private string? _selectedAction;
    private int _selectedBindingIdx = -1; // -1 = none, 0..N-1 = binding, N..N+M-1 = composite
    private bool _dirty;

    // Listening state which binding slot we're listening for
    private bool _listeningForBinding;
    private string? _listenCompositePartName;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var map = asset as InputActionMap;
        if (map == null) return;

        // Handle listen mode
        if (_listeningForBinding)
        {
            InputBindingListener.Update();
            if (!InputBindingListener.IsListening)
                _listeningForBinding = false;
        }

        // Header
        using (paper.Row($"{id}_hdr").Height(EditorTheme.RowHeight + 4).RowBetween(8).Enter())
        {
            Origami.Header(paper, $"{id}_title", $"{EditorIcons.Gamepad}  Input Actions: {map.Name}").Show();

            if (_dirty)
                Origami.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save", () => { SaveMap(map, entry); }).Width(80).Show();
        }
        Origami.Separator(paper, $"{id}_sep").Show();

        // Two-column layout
        using (paper.Row($"{id}_body").Height(UnitValue.Auto).RowBetween(4).Enter())
        {
            DrawActionList(paper, $"{id}_al", map, font);
            DrawBindingsPanel(paper, $"{id}_bp", map, font);
        }
    }

    // ================================================================
    //  Left: Action List
    // ================================================================

    private void DrawActionList(Paper paper, string id, InputActionMap map, Prowl.Scribe.FontFile font)
    {
        float fs = EditorTheme.FontSize;

        using (paper.Column(id)
            .Width(paper.Percent(28)).MinWidth(120)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Neutral400).Rounded(4)
            .Padding(4, 4, 4, 4)
            .Enter())
        {
            // Header
            using (paper.Row($"{id}_hdr").Height(20).RowBetween(4).Enter())
            {
                paper.Box($"{id}_lbl")
                    .Width(UnitValue.Stretch()).Height(20)
                    .Text("Actions", font).TextColor(EditorTheme.Ink400)
                    .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

                paper.Box($"{id}_add")
                    .Width(18).Height(18).Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                    .Text(EditorIcons.Plus, font).TextColor(EditorTheme.Ink400)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) =>
                    {
                        string name = FindUniqueName(map, "NewAction");
                        map.AddAction(name, InputActionType.Button);
                        _selectedAction = name;
                        _selectedBindingIdx = -1;
                        MarkDirty();
                    });
            }

            paper.Box($"{id}_div").Height(1).BackgroundColor(EditorTheme.Ink200).Margin(0, 3, 0, 3);

            foreach (var action in map.Actions.ToList())
            {
                bool sel = _selectedAction == action.Name;
                string icon = action.ActionType switch
                {
                    InputActionType.Button => EditorIcons.Circle,
                    InputActionType.Value => EditorIcons.ArrowsLeftRight,
                    InputActionType.PassThrough => EditorIcons.ArrowRight,
                    _ => EditorIcons.Circle
                };

                paper.Box($"{id}_{action.Name}")
                    .Height(EditorTheme.RowHeight)
                    .BackgroundColor(sel ? EditorTheme.Purple400 : Color.Transparent)
                    .Hovered.BackgroundColor(sel ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                    .Rounded(3)
                    .Text($"  {icon}  {action.Name}", font).TextColor(sel ? EditorTheme.Ink500 : EditorTheme.Ink500)
                    .FontSize(fs - 1).Alignment(TextAlignment.MiddleLeft)
                    .OnClick(action.Name, (n, _) => { _selectedAction = n; _selectedBindingIdx = -1; });
            }
        }
    }

    // ================================================================
    //  Right: Bindings Panel (bindings list + selected binding properties)
    // ================================================================

    private void DrawBindingsPanel(Paper paper, string id, InputActionMap map, Prowl.Scribe.FontFile font)
    {
        float fs = EditorTheme.FontSize;

        using (paper.Column(id)
            .Width(UnitValue.Stretch())
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Neutral400).Rounded(4)
            .Padding(6, 6, 6, 6)
            .Enter())
        {
            var action = _selectedAction != null ? map.FindAction(_selectedAction) : null;
            if (action == null)
            {
                paper.Box($"{id}_empty").Height(80)
                    .Text("Select an action", font).TextColor(EditorTheme.Ink300)
                    .FontSize(fs).Alignment(TextAlignment.MiddleCenter);
                return;
            }

            // -- Action Properties --
            EditorGUI.Row(paper, $"{id}_name", "Name", () =>
                Origami.TextField(paper, $"{id}_name_v", action.Name, v =>
                {
                    if (!string.IsNullOrWhiteSpace(v) && v != action.Name && map.FindAction(v) == null)
                    {
                        map.RemoveAction(action.Name);
                        action.Name = v;
                        map.AddAction(action);
                        _selectedAction = v;
                        MarkDirty();
                    }
                }).Show());

            EditorGUI.Row(paper, $"{id}_atype", "Type", () =>
                Origami.EnumDropdown(paper, $"{id}_atype_v", action.ActionType,
                    v => { action.ActionType = v; MarkDirty(); }).Show());

            bool isFloat2 = action.ExpectedValueType == typeof(Prowl.Vector.Float2);
            EditorGUI.Row(paper, $"{id}_vtype", "Value", () =>
                Origami.Dropdown(paper, $"{id}_vtype_v", isFloat2 ? 1 : 0,
                    v => { action.ExpectedValueType = v == 1 ? typeof(Prowl.Vector.Float2) : typeof(float); MarkDirty(); },
                    new[] { "float", "Float2" }).Show());

            paper.Box($"{id}_sp1").Height(6);
            Origami.Separator(paper, $"{id}_bsep").Show();

            // -- Bindings List --
            using (paper.Row($"{id}_bhdr").Height(20).RowBetween(4).Enter())
            {
                paper.Box($"{id}_blbl")
                    .Width(UnitValue.Stretch()).Height(20)
                    .Text("Bindings", font).TextColor(EditorTheme.Ink400)
                    .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);
            }

            // Regular bindings
            for (int i = 0; i < action.Bindings.Count; i++)
            {
                int idx = i;
                var binding = action.Bindings[i];
                bool sel = _selectedBindingIdx == i;

                DrawBindingRow(paper, $"{id}_br{i}", binding, sel, () =>
                {
                    _selectedBindingIdx = sel ? -1 : idx;
                    _listenCompositePartName = null;
                }, () =>
                {
                    action.Bindings.RemoveAt(idx);
                    if (_selectedBindingIdx >= action.Bindings.Count) _selectedBindingIdx = -1;
                    MarkDirty();
                });

                // Inline properties when selected
                if (sel)
                    DrawBindingProperties(paper, $"{id}_bp{i}", binding, action, i);
            }

            // Composite bindings
            int bindingOffset = action.Bindings.Count;
            for (int i = 0; i < action.CompositeBindings.Count; i++)
            {
                int idx = i;
                int selIdx = bindingOffset + i;
                var composite = action.CompositeBindings[i];
                bool sel = _selectedBindingIdx == selIdx;

                DrawCompositeRow(paper, $"{id}_cr{i}", composite, font, fs, sel, () =>
                {
                    _selectedBindingIdx = sel ? -1 : selIdx;
                    _listenCompositePartName = null;
                }, () =>
                {
                    action.RemoveCompositeAt(idx);
                    if (_selectedBindingIdx >= bindingOffset + action.CompositeBindings.Count)
                        _selectedBindingIdx = -1;
                    MarkDirty();
                });

                // Inline properties when selected
                if (sel)
                    DrawCompositeProperties(paper, $"{id}_cp{i}", composite, action);
            }

            paper.Box($"{id}_sp2").Height(6);

            // -- Add Buttons --
            using (paper.Row($"{id}_add1").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
            {
                Origami.Button(paper, $"{id}_abind", $"{EditorIcons.Plus} Add Binding", () =>
                {
                    action.AddBinding(KeyCode.Space);
                    _selectedBindingIdx = action.Bindings.Count - 1;
                    MarkDirty();
                }).Width(110).Show();

                Origami.Button(paper, $"{id}_awasd", $"{EditorIcons.Plus} WASD", () =>
                {
                    action.AddBinding(new Vector2CompositeBinding(
                        InputBinding.CreateKeyBinding(KeyCode.W),
                        InputBinding.CreateKeyBinding(KeyCode.S),
                        InputBinding.CreateKeyBinding(KeyCode.A),
                        InputBinding.CreateKeyBinding(KeyCode.D)));
                    _selectedBindingIdx = action.Bindings.Count + action.CompositeBindings.Count - 1;
                    MarkDirty();
                }).Width(80).Show();

                Origami.Button(paper, $"{id}_aaxis", $"{EditorIcons.Plus} Axis", () =>
                {
                    action.AddBinding(new AxisCompositeBinding(
                        InputBinding.CreateKeyBinding(KeyCode.D),
                        InputBinding.CreateKeyBinding(KeyCode.A)));
                    _selectedBindingIdx = action.Bindings.Count + action.CompositeBindings.Count - 1;
                    MarkDirty();
                }).Width(75).Show();
            }

            paper.Box($"{id}_sp3").Height(12);

            Origami.Button(paper, $"{id}_del", $"{EditorIcons.Trash}  Delete Action", () =>
            {
                map.RemoveAction(action.Name);
                _selectedAction = null;
                _selectedBindingIdx = -1;
                MarkDirty();
            }).Width(130).Show();
        }
    }

    // ================================================================
    //  Binding Row (summary line)
    // ================================================================

    private void DrawBindingRow(Paper paper, string id, InputBinding binding, bool selected,
        Action onSelect, Action onRemove)
    {
        var font = EditorTheme.DefaultFont!;
        float fs = EditorTheme.FontSize;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .BackgroundColor(selected ? Color.FromArgb(60, EditorTheme.Purple400) : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(selected ? Color.FromArgb(80, EditorTheme.Purple400) : EditorTheme.Ink200).End()
            .Rounded(3).Margin(0, 0, 0, 1)
            .ChildLeft(8).RowBetween(4)
            .OnClick(0, (_, _) => onSelect())
            .Enter())
        {
            paper.Box($"{id}_lbl")
                .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text(GetBindingSummary(binding), font).TextColor(EditorTheme.Ink500)
                .FontSize(fs - 1).Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_x")
                .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, e) => { e.StopPropagation(); onRemove(); });
        }
    }

    private void DrawCompositeRow(Paper paper, string id, InputCompositeBinding composite,
        Prowl.Scribe.FontFile font, float fs, bool selected, Action onSelect, Action onRemove)
    {
        string label = composite switch
        {
            Vector2CompositeBinding => "Vector2 Composite",
            AxisCompositeBinding => "Axis Composite",
            DualAxisCompositeBinding => "Dual Axis Composite",
            _ => "Composite"
        };

        // Build a summary of parts
        string parts = string.Join(", ", composite.Parts.Select(p => GetPartSummary(p.Value)));

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .BackgroundColor(selected ? Color.FromArgb(60, EditorTheme.Purple400) : Color.FromArgb(30, EditorTheme.Purple400))
            .Hovered.BackgroundColor(selected ? Color.FromArgb(80, EditorTheme.Purple400) : Color.FromArgb(50, EditorTheme.Purple400)).End()
            .BorderColor(EditorTheme.Purple300).BorderWidth(selected ? 1 : 0)
            .Rounded(3).Margin(0, 0, 0, 1)
            .ChildLeft(8).RowBetween(4)
            .OnClick(0, (_, _) => onSelect())
            .Enter())
        {
            paper.Box($"{id}_lbl")
                .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text($"{label}  ({parts})", font).TextColor(EditorTheme.Purple400)
                .FontSize(fs - 1).Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_x")
                .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, e) => { e.StopPropagation(); onRemove(); });
        }
    }

    // ================================================================
    //  Binding Properties (inline, shown below selected binding)
    // ================================================================

    private void DrawBindingProperties(Paper paper, string id, InputBinding binding, InputAction action, int bindingIdx)
    {
        using (paper.Column(id)
            .Height(UnitValue.Auto)
            .BackgroundColor(Color.FromArgb(30, EditorTheme.Purple400))
            .Rounded(3).Margin(12, 0, 0, 2)
            .Padding(8, 8, 6, 6)
            .Enter())
        {
            // "Listen" button press any key to rebind
            DrawListenButton(paper, $"{id}_listen", binding, null);

            paper.Box($"{id}_sp").Height(4);

            // Binding type
            EditorGUI.Row(paper, $"{id}_btype", "Input Type", () =>
                Origami.EnumDropdown(paper, $"{id}_btype_v", binding.BindingType, v =>
                {
                    binding.BindingType = v;
                    // Reset to sensible defaults
                    switch (v)
                    {
                        case InputBindingType.Key: binding.Key = KeyCode.Space; break;
                        case InputBindingType.MouseButton: binding.MouseButton = MouseButton.Left; break;
                        case InputBindingType.GamepadButton: binding.GamepadButton = GamepadButton.A; break;
                        case InputBindingType.MouseAxis: binding.AxisIndex = 0; break;
                        case InputBindingType.GamepadAxis: binding.AxisIndex = 0; break;
                        case InputBindingType.GamepadTrigger: binding.AxisIndex = 0; break;
                    }
                    MarkDirty();
                }).Show());

            // Type-specific fields
            DrawBindingControl(paper, $"{id}_ctrl", binding);

            paper.Box($"{id}_isep").Height(4);
            Origami.Separator(paper, $"{id}_isep2").Show();

            // Interaction
            EditorGUI.Row(paper, $"{id}_inter", "Interaction", () =>
                Origami.EnumDropdown(paper, $"{id}_inter_v", binding.Interaction,
                    v => { binding.Interaction = v; MarkDirty(); }).Show());

            DrawInteractionParams(paper, $"{id}_ip", binding);

            paper.Box($"{id}_psep").Height(4);
            Origami.Separator(paper, $"{id}_psep2").Show();

            // Processors
            DrawProcessorList(paper, $"{id}_proc", binding.Processors);
        }
    }

    private void DrawCompositeProperties(Paper paper, string id, InputCompositeBinding composite, InputAction action)
    {
        using (paper.Column(id)
            .Height(UnitValue.Auto)
            .BackgroundColor(Color.FromArgb(30, EditorTheme.Purple400))
            .Rounded(3).Margin(12, 0, 0, 2)
            .Padding(8, 8, 6, 6)
            .Enter())
        {
            // Vector2-specific options
            if (composite is Vector2CompositeBinding v2c)
            {
                Origami.Checkbox(paper, $"{id}_norm", v2c.Normalize,
                        v => { v2c.Normalize = v; MarkDirty(); })
                    .LabelRight("Normalize").Show();
                paper.Box($"{id}_sp").Height(4);
            }

            // Each part with a listen button
            var partNames = composite.Parts.Keys.ToList();
            for (int i = 0; i < partNames.Count; i++)
            {
                string partName = partNames[i];
                var partBinding = composite.Parts[partName];

                Origami.Header(paper, $"{id}_ph{i}", $"{partName.ToUpper()}").Show();

                DrawListenButton(paper, $"{id}_pl{i}", partBinding, partName);
                DrawBindingControl(paper, $"{id}_pc{i}", partBinding);

                if (i < partNames.Count - 1)
                    paper.Box($"{id}_psep{i}").Height(4);
            }

            paper.Box($"{id}_procsep").Height(4);
            Origami.Separator(paper, $"{id}_procsep2").Show();

            // Composite-level processors
            DrawProcessorList(paper, $"{id}_proc", composite.Processors);
        }
    }

    // ================================================================
    //  Listen Button "Press any key to bind"
    // ================================================================

    private void DrawListenButton(Paper paper, string id, InputBinding binding, string? compositePartName)
    {
        var font = EditorTheme.DefaultFont!;
        bool isThisListening = _listeningForBinding && _listenCompositePartName == compositePartName;

        if (isThisListening)
        {
            paper.Box($"{id}_btn")
                .Height(EditorTheme.RowHeight)
                .BackgroundColor(EditorTheme.Purple300)
                .Rounded(3)
                .Text("  Press any key...  (Esc to cancel)", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) =>
                {
                    InputBindingListener.Cancel();
                    _listeningForBinding = false;
                });
        }
        else
        {
            string current = GetBindingSummary(binding);
            Origami.Button(paper, $"{id}_btn", $"{EditorIcons.Bullseye}  Listen  ({current})", () =>
            {
                _listenCompositePartName = compositePartName;
                _listeningForBinding = true;
                InputBindingListener.Start(detected =>
                {
                    binding.BindingType = detected.BindingType;
                    binding.Key = detected.Key;
                    binding.MouseButton = detected.MouseButton;
                    binding.GamepadButton = detected.GamepadButton;
                    binding.AxisIndex = detected.AxisIndex;
                    binding.RequiredDeviceIndex = detected.RequiredDeviceIndex;
                    _listeningForBinding = false;
                    MarkDirty();
                });
            }).Show();
        }
    }

    // ================================================================
    //  Binding Control Fields (type-specific)
    // ================================================================

    private void DrawBindingControl(Paper paper, string id, InputBinding binding)
    {
        switch (binding.BindingType)
        {
            case InputBindingType.Key:
                EditorGUI.Row(paper, $"{id}_key", "Key", () =>
                    Origami.EnumDropdown(paper, $"{id}_key_v", binding.Key ?? KeyCode.Unknown,
                        v => { binding.Key = v; MarkDirty(); }).Searchable().Show());
                break;

            case InputBindingType.MouseButton:
                EditorGUI.Row(paper, $"{id}_mb", "Button", () =>
                    Origami.EnumDropdown(paper, $"{id}_mb_v", binding.MouseButton ?? MouseButton.Left,
                        v => { binding.MouseButton = v; MarkDirty(); }).Show());
                break;

            case InputBindingType.MouseAxis:
                EditorGUI.Row(paper, $"{id}_ma", "Axis", () =>
                    Origami.Dropdown(paper, $"{id}_ma_v", binding.AxisIndex ?? 0,
                        v => { binding.AxisIndex = v; MarkDirty(); },
                        new[] { "X", "Y", "Wheel" }).Show());
                break;

            case InputBindingType.GamepadButton:
                EditorGUI.Row(paper, $"{id}_gb", "Button", () =>
                    Origami.EnumDropdown(paper, $"{id}_gb_v", binding.GamepadButton ?? GamepadButton.A,
                        v => { binding.GamepadButton = v; MarkDirty(); }).Show());
                DrawDeviceField(paper, $"{id}_gbd", binding);
                break;

            case InputBindingType.GamepadAxis:
                EditorGUI.Row(paper, $"{id}_ga", "Stick", () =>
                    Origami.Dropdown(paper, $"{id}_ga_v", binding.AxisIndex ?? 0,
                        v => { binding.AxisIndex = v; MarkDirty(); },
                        new[] { "Left Stick", "Right Stick" }).Show());
                DrawDeviceField(paper, $"{id}_gad", binding);
                break;

            case InputBindingType.GamepadTrigger:
                EditorGUI.Row(paper, $"{id}_gt", "Trigger", () =>
                    Origami.Dropdown(paper, $"{id}_gt_v", binding.AxisIndex ?? 0,
                        v => { binding.AxisIndex = v; MarkDirty(); },
                        new[] { "Left", "Right" }).Show());
                DrawDeviceField(paper, $"{id}_gtd", binding);
                break;
        }
    }

    private void DrawDeviceField(Paper paper, string id, InputBinding binding)
    {
        EditorGUI.Row(paper, $"{id}_dev", "Device Index", () =>
            Origami.NumericField<int>(paper, $"{id}_dev_v", binding.RequiredDeviceIndex ?? 0,
                v => { binding.RequiredDeviceIndex = Math.Max(0, v); MarkDirty(); }).Min(0).Show());
    }

    private void DrawInteractionParams(Paper paper, string id, InputBinding binding)
    {
        switch (binding.Interaction)
        {
            case InputInteractionType.Hold:
                EditorGUI.Row(paper, $"{id}_hd", "Hold Duration (s)", () =>
                    Origami.NumericField<float>(paper, $"{id}_hd_v", binding.HoldDuration,
                        v => { binding.HoldDuration = MathF.Max(0.01f, v); MarkDirty(); }).Show());
                break;

            case InputInteractionType.Tap:
                EditorGUI.Row(paper, $"{id}_td", "Max Tap Duration (s)", () =>
                    Origami.NumericField<float>(paper, $"{id}_td_v", binding.MaxTapDuration,
                        v => { binding.MaxTapDuration = MathF.Max(0.01f, v); MarkDirty(); }).Show());
                break;

            case InputInteractionType.MultiTap:
                EditorGUI.Row(paper, $"{id}_tc", "Tap Count", () =>
                    Origami.NumericField<int>(paper, $"{id}_tc_v", binding.TapCount,
                        v => { binding.TapCount = Math.Max(2, v); MarkDirty(); }).Min(2).Show());
                EditorGUI.Row(paper, $"{id}_tw", "Tap Window (s)", () =>
                    Origami.NumericField<float>(paper, $"{id}_tw_v", binding.TapWindow,
                        v => { binding.TapWindow = MathF.Max(0.01f, v); MarkDirty(); }).Show());
                EditorGUI.Row(paper, $"{id}_mtd", "Max Tap Duration (s)", () =>
                    Origami.NumericField<float>(paper, $"{id}_mtd_v", binding.MaxTapDuration,
                        v => { binding.MaxTapDuration = MathF.Max(0.01f, v); MarkDirty(); }).Show());
                break;
        }
    }

    // ================================================================
    //  Processor List Editor
    // ================================================================

    private static readonly string[] _processorTypes = ["Normalize", "Invert", "Scale", "Clamp", "Deadzone", "Exponential"];

    private void DrawProcessorList(Paper paper, string id, List<IInputProcessor> processors)
    {
        var font = EditorTheme.DefaultFont!;
        float fs = EditorTheme.FontSize;

        paper.Box($"{id}_lbl").Height(18)
            .Text("Processors", font).TextColor(EditorTheme.Ink400)
            .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

        for (int i = 0; i < processors.Count; i++)
        {
            int idx = i;
            var proc = processors[i];

            using (paper.Column($"{id}_p{i}")
                .Height(UnitValue.Auto)
                .BackgroundColor(EditorTheme.Neutral300).Rounded(3)
                .Margin(0, 0, 0, 2)
                .Padding(6, 6, 3, 3)
                .Enter())
            {
                // Header with name and remove button
                using (paper.Row($"{id}_ph{i}").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
                {
                    string name = proc switch
                    {
                        NormalizeProcessor => "Normalize",
                        InvertProcessor => "Invert",
                        ScaleProcessor => "Scale",
                        ClampProcessor => "Clamp",
                        DeadzoneProcessor => "Deadzone",
                        ExponentialProcessor => "Exponential",
                        _ => proc.GetType().Name
                    };

                    paper.Box($"{id}_pn{i}")
                        .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                        .Text(name, font).TextColor(EditorTheme.Ink500)
                        .FontSize(fs - 1).Alignment(TextAlignment.MiddleLeft);

                    paper.Box($"{id}_px{i}")
                        .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(idx, (ci, _) => { processors.RemoveAt(ci); MarkDirty(); });
                }

                // Processor-specific parameters
                switch (proc)
                {
                    case ScaleProcessor sp:
                        EditorGUI.Row(paper, $"{id}_ps{i}", "Scale", () =>
                            Origami.NumericField<float>(paper, $"{id}_ps{i}_v", sp.Scale,
                                v => { sp.Scale = v; MarkDirty(); }).Show());
                        break;

                    case ClampProcessor cp:
                        EditorGUI.Row(paper, $"{id}_pmn{i}", "Min", () =>
                            Origami.NumericField<float>(paper, $"{id}_pmn{i}_v", cp.Min,
                                v => { cp.Min = v; MarkDirty(); }).Show());
                        EditorGUI.Row(paper, $"{id}_pmx{i}", "Max", () =>
                            Origami.NumericField<float>(paper, $"{id}_pmx{i}_v", cp.Max,
                                v => { cp.Max = v; MarkDirty(); }).Show());
                        break;

                    case DeadzoneProcessor dp:
                        EditorGUI.Row(paper, $"{id}_pd{i}", "Threshold", () =>
                            Origami.NumericField<float>(paper, $"{id}_pd{i}_v", dp.Threshold,
                                v => { dp.Threshold = MathF.Max(0f, v); MarkDirty(); }).Min(0f).Show());
                        break;

                    case ExponentialProcessor ep:
                        EditorGUI.Row(paper, $"{id}_pe{i}", "Exponent", () =>
                            Origami.NumericField<float>(paper, $"{id}_pe{i}_v", ep.Exponent,
                                v => { ep.Exponent = MathF.Max(0.1f, v); MarkDirty(); }).Min(0.1f).Show());
                        break;
                }
            }
        }

        // Add processor dropdown
        EditorGUI.Row(paper, $"{id}_add", "Add Processor", () =>
            Origami.Dropdown(paper, $"{id}_add_v", -1,
                v =>
                {
                    IInputProcessor? newProc = v switch
                    {
                        0 => new NormalizeProcessor(),
                        1 => new InvertProcessor(),
                        2 => new ScaleProcessor(1f),
                        3 => new ClampProcessor(0f, 1f),
                        4 => new DeadzoneProcessor(0.2f),
                        5 => new ExponentialProcessor(2f),
                        _ => null
                    };
                    if (newProc != null) { processors.Add(newProc); MarkDirty(); }
                }, _processorTypes).Placeholder("Select a processor...").Show());
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static string GetBindingSummary(InputBinding binding)
    {
        return binding.BindingType switch
        {
            InputBindingType.Key => $"{binding.Key}",
            InputBindingType.MouseButton => $"Mouse {binding.MouseButton}",
            InputBindingType.MouseAxis => binding.AxisIndex switch { 0 => "Mouse X", 1 => "Mouse Y", _ => "Mouse Wheel" },
            InputBindingType.GamepadButton => $"Gamepad {binding.GamepadButton}",
            InputBindingType.GamepadAxis => binding.AxisIndex == 0 ? "Left Stick" : "Right Stick",
            InputBindingType.GamepadTrigger => binding.AxisIndex == 0 ? "Left Trigger" : "Right Trigger",
            _ => binding.BindingType.ToString()
        };
    }

    private static string GetPartSummary(InputBinding binding)
    {
        return binding.BindingType switch
        {
            InputBindingType.Key => binding.Key?.ToString() ?? "?",
            InputBindingType.GamepadButton => binding.GamepadButton?.ToString() ?? "?",
            _ => GetBindingSummary(binding)
        };
    }

    private void MarkDirty() => _dirty = true;

    private void SaveMap(InputActionMap map, AssetEntry entry)
    {
        if (Project.Current == null) return;
        string absolutePath = Path.Combine(Project.Current.AssetsPath, entry.Path);

        var savedId = map.AssetID;
        map.AssetID = Guid.Empty;
        var echo = Serializer.Serialize(typeof(object), map);
        map.AssetID = savedId;

        if (echo != null)
        {
            File.WriteAllText(absolutePath, echo.WriteToString());
            EditorAssetDatabase.Instance?.Reimport(entry.Guid);
        }
        _dirty = false;
    }

    private static string FindUniqueName(InputActionMap map, string baseName)
        => Utils.UniqueNames.MakeUnique(baseName, n => map.FindAction(n) != null,
            openSeparator: "", closeSeparator: "", stripExistingSuffix: false);
}
