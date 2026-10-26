using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using Segment = Prowl.Editor.Inspector.InputBindingDisplay.Segment;

namespace Prowl.Editor.Inspector;

/// <summary>
/// The panel the input action editor opens to add a binding, or to edit one binding or composite in full.
/// It sits in the middle of the inspector, over a dimmed copy of it, and never leaves the inspector's bounds.
/// </summary>
internal sealed class InputBindingPopup
{
    private const float HeaderHeight = 44f;
    private const float FooterHeight = 52f;
    private const float MaxWidth = 820f;
    private const float TwoColumnWidth = 640f;
    private const float Inset = 12f;

    private static readonly string[] s_processorTypes = ["Deadzone", "Scale", "Invert", "Clamp", "Normalize", "Exponential"];
    private static readonly object s_addTarget = new();

    private InputActionMap? _map;
    private InputAction? _action;
    private object? _entry;
    private bool _adding;
    private InputDevice _addDevice = InputDevice.Keyboard;
    private string _keyFilter = "";
    private string? _pickPart;
    private bool _confirmRemove;
    private float _bodyHeight = 240f;
    private object? _listenOwner;
    private string? _listenPart;

    public bool IsOpen => _action != null;
    public InputActionMap? Map => _map;

    /// <summary>Whether the popover belongs to this binding, or to this action's Add binding when <paramref name="entry"/> is null.</summary>
    public bool IsOpenFor(InputAction action, object? entry)
        => _action == action && (entry == null ? _adding : !_adding && ReferenceEquals(_entry, entry));

    public void OpenAdd(InputActionMap map, InputAction action) => Open(map, action, null);

    public void OpenEdit(InputActionMap map, InputAction action, object entry) => Open(map, action, entry);

    private void Open(InputActionMap map, InputAction action, object? entry)
    {
        Close();
        _map = map;
        _action = action;
        _entry = entry;
        _adding = entry == null;
    }

    public void Close()
    {
        StopListening();
        _map = null;
        _action = null;
        _entry = null;
        _adding = false;
        _keyFilter = "";
        _pickPart = null;
        _confirmRemove = false;
    }

    // ================================================================
    //  Frame
    // ================================================================

    /// <summary>Draws the backdrop and panel over the inspector's visible area of <paramref name="areaWidth"/> by <paramref name="areaHeight"/>.</summary>
    public void Draw(Paper paper, string id, float areaWidth, float areaHeight)
    {
        if (_map.IsNotValid() || _action == null || !_map.Actions.Contains(_action) || (_entry != null && !Owns(_action, _entry)))
        {
            Close();
            return;
        }

        if (InputBindingListener.IsListening)
        {
            InputBindingListener.Update();
            if (!InputBindingListener.IsListening) _listenOwner = null;
        }
        else if (paper.IsKeyPressed(PaperKey.Escape))
        {
            Close();
            return;
        }

        InputAction action = _action;
        OrigamiMetrics m = Origami.Current.Metrics;
        float width = MathF.Min(MaxWidth, areaWidth - Inset * 2f);
        float bodyHeight = MathF.Min(_bodyHeight, areaHeight - Inset * 2f - HeaderHeight - FooterHeight);
        bool wide = width >= TwoColumnWidth;

        using IDisposable backdrop = paper.Box(id)
            .PositionType(PositionType.SelfDirected).Position(0, 0).Size(areaWidth, areaHeight)
            .Layer(Layer.Overlay)
            .Clip()
            .BackgroundColor(Color.FromArgb(150, 6, 5, 10))
            .StopEventPropagation()
            .OnClick(0, (_, _) => { if (!InputBindingListener.IsListening) Close(); })
            .Enter();

        using (paper.Column("ibp_pop")
            .Width(width).Height(UnitValue.Auto)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Popover)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .DropShadow(0, 18, 48, -6, EditorTheme.Shadow)
            .Rounded(m.ContainerRounding + 2)
            .StopEventPropagation()
            .Enter())
        {
            DrawHeader(paper, action);

            Origami.ScrollView(paper, "ibp_scroll", width - 2f, MathF.Max(120f, bodyHeight)).Padding(0).Body(() =>
            {
                using (paper.Column("ibp_body").Height(UnitValue.Auto).Padding(18, 18, 14, 18).Gap(10)
                    .OnPostLayout((_, rect) => _bodyHeight = (float)rect.Size.Y)
                    .Enter())
                {
                    switch (_entry)
                    {
                        case InputBinding binding:
                            Columns(paper, wide,
                                () => DrawBindingControl(paper, action, binding),
                                () => DrawBehaviour(paper, action, binding, binding.Processors,
                                    InputBindingDisplay.IsAnalog(binding), InputBindingDisplay.GivesVector(binding)));
                            break;
                        case InputCompositeBinding composite:
                            Columns(paper, wide,
                                () => DrawCompositeParts(paper, composite),
                                () => DrawBehaviour(paper, action, composite, composite.Processors,
                                    analog: true, InputBindingDisplay.GivesVector(composite)));
                            break;
                        default:
                            DrawAdd(paper, action);
                            break;
                    }
                }
            });

            DrawFooter(paper, action);
        }
    }

    /// <summary>Two columns side by side when there is room, otherwise one under the other.</summary>
    private static void Columns(Paper paper, bool wide, Action left, Action right)
    {
        if (!wide)
        {
            left();
            right();
            return;
        }

        using (paper.Row("ibp_cols").Height(UnitValue.Auto).Gap(22).Enter())
        {
            using (paper.Column("ibp_col_l").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(10).Enter())
                left();
            using (paper.Column("ibp_col_r").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(10).Enter())
                right();
        }
    }

    private static void DrawBehaviour(Paper paper, InputAction action, IInputInteractionSettings settings,
        List<IInputProcessor> processors, bool analog, bool givesVector)
    {
        DrawInteraction(paper, action, settings, analog, givesVector);
        DrawProcessors(paper, processors);
    }

    private static bool Owns(InputAction action, object entry) => entry switch
    {
        InputBinding b => action.Bindings.Contains(b),
        InputCompositeBinding c => action.CompositeBindings.Contains(c),
        _ => false
    };

    private void DrawHeader(Paper paper, InputAction action)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        string title = _entry switch
        {
            InputBinding => "Binding",
            InputCompositeBinding c => InputBindingDisplay.CompositeName(c),
            _ => "Add binding"
        };

        using (paper.Row("ibp_header").Height(HeaderHeight).Padding(14, 8, 0, 0).Gap(8)
            .BackgroundColor(EditorTheme.Neutral200).RoundedTop(m.ContainerRounding).Enter())
        {
            paper.Box("ibp_title").Width(UnitValue.Auto).Height(HeaderHeight)
                .Text(title, EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box("ibp_subtitle").Height(HeaderHeight)
                .Text(action.Name, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink300)
                .FontSize(InputBindingDisplay.LabelSize + 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            InputBindingDisplay.IconBox(paper, "ibp_close", EditorIcons.Xmark, Close, tooltip: "Close");
        }
    }

    private void DrawFooter(Paper paper, InputAction action)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        using (paper.Row("ibp_footer").Height(FooterHeight).Padding(14, 14, 0, 0).Gap(6)
            .BackgroundColor(EditorTheme.Neutral200).RoundedBottom(m.ContainerRounding).Enter())
        {
            if (_entry != null && _confirmRemove)
            {
                InputBindingDisplay.Caption(paper, "ibp_rm_q", "Remove this binding?");
                InputBindingDisplay.SmallButton(paper, "ibp_rm_yes", "Remove", () => Remove(action), EditorTheme.Red400);
                InputBindingDisplay.SmallButton(paper, "ibp_rm_no", "Keep", () => _confirmRemove = false);
            }
            else if (_entry != null)
            {
                InputBindingDisplay.SmallButton(paper, "ibp_rm", $"{EditorIcons.Trash}  Remove", () => _confirmRemove = true, EditorTheme.Red400);
            }

            paper.Box("ibp_fspacer").Height(1).IsNotInteractable();
            InputBindingDisplay.SmallButton(paper, "ibp_done", _entry == null ? "Cancel" : "Done", Close, primary: _entry != null);
        }
    }

    private void Remove(InputAction action)
    {
        switch (_entry)
        {
            case InputBinding b: action.Bindings.Remove(b); break;
            case InputCompositeBinding c: action.RemoveCompositeAt(action.CompositeBindings.ToList().IndexOf(c)); break;
        }
        Close();
    }

    // ================================================================
    //  Add
    // ================================================================

    private void DrawAdd(Paper paper, InputAction action)
    {
        DrawListenArea(paper, action);

        bool wantsVector = InputBindingDisplay.OutputsVector(action);
        if (InputBindingDisplay.GetKind(action) != ActionKind.Button)
        {
            InputBindingDisplay.FieldHeading(paper, "ibp_presets_h", $"Composites for {(wantsVector ? "Vector2" : "Axis")}");
            using (paper.Row("ibp_presets").Height(UnitValue.Auto).Gap(6).LineGap(6).WrapContent().Enter())
            {
                int i = 0;
                foreach ((string label, string description, Func<object> make) in Presets(wantsVector))
                    PresetCard(paper, $"ibp_preset{i++}", label, description, make(), () => AddEntry(action, make()));
            }
        }

        InputBindingDisplay.FieldHeading(paper, "ibp_single_h", "Or pick a single control");
        DeviceSelector(paper, "ibp_devices", _addDevice, d => { _addDevice = d; _keyFilter = ""; });

        if (_addDevice == InputDevice.Keyboard)
            KeyGrid(paper, "ibp_addkeys", null, key => AddEntry(action, InputBinding.CreateKeyBinding(key)), anyKey: () =>
                AddEntry(action, new InputBinding { BindingType = InputBindingType.AnyKey }), anyKeyOn: false);
        else
            OptionGrid(paper, "ibp_addopts", _addDevice, wantsVector, null, option =>
            {
                var binding = new InputBinding();
                option.Apply(binding);
                if (option.GivesVector) binding.Processors.Add(new DeadzoneProcessor(0.2f));
                AddEntry(action, binding);
            });

        if (_addDevice == InputDevice.XR)
            InputBindingDisplay.Hint(paper, "ibp_xrhint", "No headset needed. It is added for the right hand, change the hand on the binding after.");
    }

    private void DrawListenArea(Paper paper, InputAction action)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        bool listening = IsListeningFor(s_addTarget, null);

        using (paper.Column("ibp_listen").Height(76).Rounded(m.ContainerRounding)
            .BackgroundColor(listening ? EditorTheme.WithAlpha(EditorTheme.Accent, 40) : EditorTheme.Neutral200)
            .BorderColor(listening ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(listening ? EditorTheme.Accent : EditorTheme.BorderStrong).End()
            .Cursor(PaperCursor.Pointer)
            .OnClick(0, (_, _) => StartListening(s_addTarget, null, detected =>
            {
                var binding = new InputBinding();
                InputBindingDisplay.CopyControl(binding, detected);
                AddEntry(action, binding);
            }))
            .Enter())
        {
            paper.Box("ibp_listen_t").Height(UnitValue.Stretch()).Margin(0, 0, 12, 2)
                .Text($"{EditorIcons.Bullseye}   {(listening ? "Press a key, mouse button or gamepad button" : "Listen for input")}",
                    EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.BottomCenter).IsNotInteractable();
            paper.Box("ibp_listen_s").Height(UnitValue.Stretch()).Margin(0, 0, 2, 12)
                .Text(listening ? "Esc cancels" : "Press the control you want and it is added", EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink300).FontSize(InputBindingDisplay.LabelSize + 1).Alignment(TextAlignment.Center).IsNotInteractable();
        }
    }

    private static void PresetCard(Paper paper, string id, string label, string description, object preview, Action onClick)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        using (paper.Column(id).Width(UnitValue.Auto).MinWidth(124).Height(UnitValue.Auto).Padding(10, 10, 8, 8).Gap(6)
            .Rounded(m.Rounding).BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(EditorTheme.Accent).End()
            .Cursor(PaperCursor.Pointer)
            .OnClick(0, (_, _) => onClick())
            .Enter())
        {
            paper.Box($"{id}_t").Width(UnitValue.Auto).Height(20)
                .Text(label, EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink500)
                .FontSize(InputBindingDisplay.LabelSize + 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            if (preview is InputCompositeBinding composite)
                InputBindingDisplay.CompositeSummary(paper, $"{id}_p", composite);
            else if (preview is InputBinding binding)
                InputBindingDisplay.Keycap(paper, $"{id}_p", InputBindingDisplay.ControlName(binding),
                    InputBindingDisplay.DeviceColor(InputBindingDisplay.GetDevice(binding)));

            paper.Box($"{id}_d").Width(UnitValue.Auto).Height(18)
                .Text(description, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink300)
                .FontSize(InputBindingDisplay.LabelSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
        }
    }

    private static IEnumerable<(string, string, Func<object>)> Presets(bool vector)
    {
        if (vector)
        {
            yield return ("WASD", "Four keys, normalized", () => FourKeys(KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
            yield return ("Arrow keys", "Four keys, normalized", () => FourKeys(KeyCode.Up, KeyCode.Down, KeyCode.Left, KeyCode.Right));
            yield return ("Mouse delta", "Dual axis X and Y", () => new DualAxisCompositeBinding(InputBinding.CreateMouseAxisBinding(0), InputBinding.CreateMouseAxisBinding(1)));
            yield return ("Left stick", "With 0.2 deadzone", () => new InputBinding
            {
                BindingType = InputBindingType.GamepadAxis, AxisIndex = 0, Processors = [new DeadzoneProcessor(0.2f)]
            });
        }
        else
        {
            yield return ("A and D", "Negative and positive", () => new AxisCompositeBinding(InputBinding.CreateKeyBinding(KeyCode.D), InputBinding.CreateKeyBinding(KeyCode.A)));
            yield return ("Q and E", "Negative and positive", () => new AxisCompositeBinding(InputBinding.CreateKeyBinding(KeyCode.E), InputBinding.CreateKeyBinding(KeyCode.Q)));
            yield return ("Mouse wheel", "Scroll delta", () => InputBinding.CreateMouseAxisBinding(2));
            yield return ("Right trigger", "0 to 1", () => new InputBinding { BindingType = InputBindingType.GamepadTrigger, AxisIndex = 1 });
        }
    }

    private static Vector2CompositeBinding FourKeys(KeyCode up, KeyCode down, KeyCode left, KeyCode right) => new(
        InputBinding.CreateKeyBinding(up), InputBinding.CreateKeyBinding(down),
        InputBinding.CreateKeyBinding(left), InputBinding.CreateKeyBinding(right));

    private void AddEntry(InputAction action, object entry)
    {
        StopListening();
        switch (entry)
        {
            case InputBinding b: action.AddBinding(b); break;
            case InputCompositeBinding c: action.AddBinding(c); break;
        }
        _entry = entry;
        _keyFilter = "";
    }

    // ================================================================
    //  Edit a single binding
    // ================================================================

    private void DrawBindingControl(Paper paper, InputAction action, InputBinding binding)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        InputDevice device = InputBindingDisplay.GetDevice(binding);
        bool wantsVector = InputBindingDisplay.OutputsVector(action);
        bool listening = IsListeningFor(binding, null);

        using (paper.Row("ibp_ctl").Height(UnitValue.Auto).Padding(12, 12, 10, 10).Gap(12)
            .Rounded(m.ContainerRounding).BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(listening ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            DeviceIcon(paper, "ibp_ctl_icon", device, 18f);
            InputBindingDisplay.Keycap(paper, "ibp_ctl_key", InputBindingDisplay.ControlName(binding), InputBindingDisplay.DeviceColor(device), large: true);

            using (paper.Column("ibp_ctl_meta").Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            {
                paper.Box("ibp_ctl_name").Height(20)
                    .Text(InputBindingDisplay.ControlName(binding), EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont!)
                    .TextColor(EditorTheme.Ink500).FontSize(InputBindingDisplay.LabelSize + 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box("ibp_ctl_dev").Height(18)
                    .Text(InputBindingDisplay.DeviceLabel(device), EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink300)
                    .FontSize(InputBindingDisplay.LabelSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            }

            InputBindingDisplay.SmallButton(paper, "ibp_ctl_listen", listening ? "Press now..." : $"{EditorIcons.Bullseye}  Listen",
                () => StartListening(binding, null, d => InputBindingDisplay.CopyControl(binding, d)), primary: listening);
        }

        InputBindingDisplay.FieldHeading(paper, "ibp_dev_h", "Device");
        DeviceSelector(paper, "ibp_dev", device, d =>
        {
            if (d == device) return;
            InputBindingDisplay.CopyControl(binding, InputBindingDisplay.CreateDefault(d, wantsVector));
            _keyFilter = "";
        });

        if (device == InputDevice.Gamepad)
        {
            InputBindingDisplay.FieldHeading(paper, "ibp_pad_h", "From");
            InputBindingDisplay.Segmented(paper, "ibp_pad",
                [new("Any"), new("Pad 0"), new("Pad 1"), new("Pad 2"), new("Pad 3")],
                (binding.RequiredDeviceIndex ?? -1) + 1, i => binding.RequiredDeviceIndex = i == 0 ? null : i - 1);
        }

        if (device == InputDevice.XR)
        {
            InputBindingDisplay.FieldHeading(paper, "ibp_hand_h", "Hand");
            InputBindingDisplay.Segmented(paper, "ibp_hand", [new("Left hand"), new("Right hand")],
                binding.XRHand == XRHand.Left ? 0 : 1, i => binding.XRHand = i == 0 ? XRHand.Left : XRHand.Right);
            InputBindingDisplay.Hint(paper, "ibp_xrhint", "No headset needed. Pick the hand and the control.");
        }

        InputBindingDisplay.FieldHeading(paper, "ibp_ctl_h", "Control");
        if (device == InputDevice.Keyboard)
            KeyGrid(paper, "ibp_keys", binding.BindingType == InputBindingType.Key ? binding.Key : null, key =>
            {
                InputBindingDisplay.SetControlType(binding, InputBindingType.Key);
                binding.Key = key;
            }, anyKey: () => InputBindingDisplay.SetControlType(binding, InputBindingType.AnyKey),
            anyKeyOn: binding.BindingType == InputBindingType.AnyKey);
        else
            OptionGrid(paper, "ibp_opts", device, wantsVector, binding, o => o.Apply(binding));
    }

    // ================================================================
    //  Edit a composite
    // ================================================================

    private void DrawCompositeParts(Paper paper, InputCompositeBinding composite)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        InputBindingDisplay.FieldHeading(paper, "ibp_parts_h", "Parts", "listen, or pick a key");

        int i = 0;
        foreach ((string name, InputBinding part) in composite.Parts)
        {
            string id = $"ibp_part{i++}";
            bool listening = IsListeningFor(composite, name);
            bool picking = _pickPart == name;
            InputDevice device = InputBindingDisplay.GetDevice(part);

            using (paper.Row(id).Height(UnitValue.Auto).Padding(10, 8, 6, 6).Gap(10).Rounded(m.Rounding)
                .BackgroundColor(listening || picking ? EditorTheme.WithAlpha(EditorTheme.Accent, 34) : EditorTheme.Neutral200)
                .BorderColor(listening || picking ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                paper.Box($"{id}_n").Width(70).Height(28).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text(InputBindingDisplay.PartName(name), EditorTheme.FontMedium ?? EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink400)
                    .FontSize(InputBindingDisplay.LabelSize + 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                DeviceIcon(paper, $"{id}_i", device, 16f);
                InputBindingDisplay.Keycap(paper, $"{id}_k", InputBindingDisplay.ControlName(part), InputBindingDisplay.DeviceColor(device));
                paper.Box($"{id}_sp").Height(1).IsNotInteractable();

                InputBindingDisplay.SmallButton(paper, $"{id}_pick", "Pick", () =>
                {
                    StopListening();
                    _pickPart = picking ? null : name;
                    _keyFilter = "";
                }, primary: picking);
                InputBindingDisplay.SmallButton(paper, $"{id}_l", listening ? "Press now..." : "Listen", () =>
                {
                    _pickPart = null;
                    StartListening(composite, name, d => InputBindingDisplay.CopyControl(part, d));
                }, primary: listening);
            }
        }

        if (_pickPart != null && composite.Parts.TryGetValue(_pickPart, out InputBinding? picked))
            KeyGrid(paper, "ibp_partkeys", picked.BindingType == InputBindingType.Key ? picked.Key : null, key =>
            {
                InputBindingDisplay.SetControlType(picked, InputBindingType.Key);
                picked.Key = key;
                _pickPart = null;
            }, anyKey: null, anyKeyOn: false);

        if (composite is Vector2CompositeBinding v2)
            Origami.Checkbox(paper, "ibp_norm", v2.Normalize, v => v2.Normalize = v)
                .LabelRight("Normalize, so diagonals are not faster").Show();

    }

    // ================================================================
    //  Control pickers
    // ================================================================

    private static void DeviceSelector(Paper paper, string id, InputDevice current, Action<InputDevice> setter)
    {
        InputBindingDisplay.Segmented(paper, id,
            [.. InputBindingDisplay.Devices.Select(d => new Segment($"{InputBindingDisplay.DeviceIcon(d)}  {InputBindingDisplay.DeviceLabel(d)}"))],
            Array.IndexOf(InputBindingDisplay.Devices, current), i => setter(InputBindingDisplay.Devices[i]));
    }

    /// <summary>Common keys as key caps, every key once something is typed in the filter.</summary>
    private void KeyGrid(Paper paper, string id, KeyCode? current, Action<KeyCode> onPick, Action? anyKey, bool anyKeyOn)
    {
        Origami.SearchField(paper, $"{id}_filter", _keyFilter, v => _keyFilter = v, "Filter keys").Show();

        IEnumerable<KeyCode> keys = string.IsNullOrWhiteSpace(_keyFilter)
            ? InputBindingDisplay.CommonKeys
            : InputBindingDisplay.AllKeys.Where(k => k.ToString().Contains(_keyFilter.Trim(), StringComparison.OrdinalIgnoreCase)
                || InputBindingDisplay.KeyLabel(k).Contains(_keyFilter.Trim(), StringComparison.OrdinalIgnoreCase));

        using (paper.Row(id).Height(UnitValue.Auto).Gap(5).LineGap(5).WrapContent().Enter())
        {
            if (anyKey != null && string.IsNullOrWhiteSpace(_keyFilter))
                InputBindingDisplay.KeyButton(paper, $"{id}_any", "Any Key", EditorTheme.Ink500, anyKeyOn, true, anyKey);

            foreach (KeyCode key in keys)
                InputBindingDisplay.KeyButton(paper, $"{id}_{(int)key}", InputBindingDisplay.KeyLabel(key), EditorTheme.Ink500,
                    current == key, true, () => onPick(key));
        }

        if (string.IsNullOrWhiteSpace(_keyFilter))
            InputBindingDisplay.Hint(paper, $"{id}_more", "Type in the filter to see every key.");
    }

    /// <summary>A device's controls as key caps. Ones that give the wrong value type for the action are dimmed.</summary>
    private static void OptionGrid(Paper paper, string id, InputDevice device, bool wantsVector, InputBinding? current, Action<InputControlOption> onPick)
    {
        List<InputControlOption> options = InputBindingDisplay.Options(device);
        Color color = InputBindingDisplay.DeviceColor(device);
        bool anyBlocked = false;

        using (paper.Row(id).Height(UnitValue.Auto).Gap(5).LineGap(5).WrapContent().Enter())
        {
            for (int i = 0; i < options.Count; i++)
            {
                InputControlOption option = options[i];
                bool fits = option.GivesVector == wantsVector;
                anyBlocked |= !fits;
                InputBindingDisplay.KeyButton(paper, $"{id}_{i}", option.Label, color, current != null && option.Matches(current), fits,
                    () => onPick(option), fits ? null : wantsVector ? "Gives float. A Vector2 action needs a stick or a composite." : "Gives Float2. Switch Kind to Vector2 to use it.");
            }
        }

        if (anyBlocked)
            InputBindingDisplay.Hint(paper, $"{id}_hint", wantsVector
                ? "Buttons are dimmed. A Vector2 action needs a stick, or a composite."
                : "Sticks are dimmed. They give Float2, switch Kind to Vector2 to use them.");
    }

    // ================================================================
    //  Interaction and press point
    // ================================================================

    private static void DrawInteraction(Paper paper, InputAction action, IInputInteractionSettings settings, bool analog, bool givesVector)
    {
        string badge = InputBindingDisplay.InteractionSummary(settings) + (analog ? $", press at {settings.PressPoint:0.##}" : "");

        Origami.Foldout(paper, "ibp_inter", "Triggers when").Badge(badge).Body(() =>
        {
            if (analog)
            {
                EditorGUI.Row(paper, "ibp_press", "Press point", () =>
                    Origami.Slider(paper, "ibp_press_v", settings.PressPoint, v => settings.PressPoint = v, 0.05f, 1f)
                        .Step(0.05f).ShowValue().Show());
                InputBindingDisplay.Hint(paper, "ibp_press_hint", "How far it has to go to count as pressed, for Button actions and the interactions below.");
            }

            for (int i = 0; i < InputBindingDisplay.Interactions.Length; i++)
            {
                InputInteractionType type = InputBindingDisplay.Interactions[i];
                Origami.Radio(paper, $"ibp_it{i}", settings.Interaction == type, on => { if (on) settings.Interaction = type; })
                    .Label(InputBindingDisplay.InteractionName(type))
                    .Description(InputBindingDisplay.InteractionDescription(type)).Show();

                if (settings.Interaction == type)
                    DrawInteractionParams(paper, $"ibp_ip{i}", settings);
            }

            if (settings.Interaction != InputInteractionType.Default && action.ActionType != InputActionType.Button)
                InputBindingDisplay.Hint(paper, "ibp_dir_hint", givesVector
                    ? "Performs with the direction that was pressed, so double tap A gives (-1, 0)."
                    : "Performs with the value that was pressed.");
        });
    }

    private static void DrawInteractionParams(Paper paper, string id, IInputInteractionSettings s)
    {
        switch (s.Interaction)
        {
            case InputInteractionType.Hold:
                SecondsRow(paper, $"{id}_hold", "Hold for", s.HoldDuration, v => s.HoldDuration = v);
                break;
            case InputInteractionType.Tap:
                SecondsRow(paper, $"{id}_tap", "Max press", s.MaxTapDuration, v => s.MaxTapDuration = v);
                break;
            case InputInteractionType.MultiTap:
                EditorGUI.Row(paper, $"{id}_count", "Taps", () =>
                    Origami.NumericField<int>(paper, $"{id}_count_v", s.TapCount, v => s.TapCount = Math.Max(2, v)).Min(2).Show());
                SecondsRow(paper, $"{id}_gap", "Gap under", s.TapWindow, v => s.TapWindow = v);
                SecondsRow(paper, $"{id}_tap", "Max press", s.MaxTapDuration, v => s.MaxTapDuration = v);
                break;
        }
    }

    private static void SecondsRow(Paper paper, string id, string label, float value, Action<float> setter)
    {
        EditorGUI.Row(paper, id, label, () =>
            Origami.NumericField<float>(paper, $"{id}_v", value, v => setter(MathF.Max(0.01f, v)))
                .Min(0.01f).Step(0.05f).Suffix("s").Show());
    }

    // ================================================================
    //  Processors
    // ================================================================

    private static void DrawProcessors(Paper paper, List<IInputProcessor> processors)
    {
        string badge = processors.Count switch
        {
            0 => "None",
            1 => ProcessorName(processors[0]),
            _ => $"{processors.Count}, run top to bottom"
        };
        Origami.Foldout(paper, "ibp_proc", "Processors").Badge(badge).Body(() => DrawProcessorList(paper, processors));
    }

    private static void DrawProcessorList(Paper paper, List<IInputProcessor> processors)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        for (int i = 0; i < processors.Count; i++)
        {
            int index = i;
            IInputProcessor proc = processors[i];
            string id = $"ibp_proc{i}";

            using (paper.Row(id).Height(UnitValue.Auto).MinHeight(38).Padding(10, 6, 4, 4).Gap(8)
                .Rounded(m.Rounding).BackgroundColor(EditorTheme.Neutral200)
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                InputBindingDisplay.Caption(paper, $"{id}_n", (i + 1).ToString());
                paper.Box($"{id}_t").Width(UnitValue.Auto).Height(28).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text(ProcessorName(proc), EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink500)
                    .FontSize(InputBindingDisplay.LabelSize + 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

                DrawProcessorParams(paper, id, proc);

                paper.Box($"{id}_sp").Height(1).IsNotInteractable();
                InputBindingDisplay.IconBox(paper, $"{id}_up", EditorIcons.ChevronUp, () => Swap(processors, index, index - 1), index == 0, "Move up");
                InputBindingDisplay.IconBox(paper, $"{id}_dn", EditorIcons.ChevronDown, () => Swap(processors, index, index + 1), index == processors.Count - 1, "Move down");
                InputBindingDisplay.IconBox(paper, $"{id}_x", EditorIcons.Xmark, () => processors.RemoveAt(index), tooltip: "Remove");
            }
        }

        using (paper.Row("ibp_proc_add").Height(UnitValue.Auto).Gap(5).LineGap(5).WrapContent().Enter())
        {
            foreach (string type in s_processorTypes)
                InputBindingDisplay.SmallButton(paper, $"ibp_proc_add_{type}", $"{EditorIcons.Plus}  {type}", () => processors.Add(CreateProcessor(type)));
        }
    }

    private static void DrawProcessorParams(Paper paper, string id, IInputProcessor proc)
    {
        switch (proc)
        {
            case DeadzoneProcessor dp:
                ProcessorField(paper, $"{id}_a", "threshold", dp.Threshold, v => dp.Threshold = Math.Clamp(v, 0f, 0.99f));
                break;
            case ScaleProcessor sp:
                ProcessorField(paper, $"{id}_a", "scale", sp.Scale, v => sp.Scale = v);
                break;
            case ClampProcessor cp:
                ProcessorField(paper, $"{id}_a", "min", cp.Min, v => cp.Min = v);
                ProcessorField(paper, $"{id}_b", "max", cp.Max, v => cp.Max = v);
                break;
            case ExponentialProcessor ep:
                ProcessorField(paper, $"{id}_a", "exponent", ep.Exponent, v => ep.Exponent = MathF.Max(0.1f, v));
                break;
        }
    }

    private static void ProcessorField(Paper paper, string id, string label, float value, Action<float> setter)
    {
        InputBindingDisplay.Caption(paper, $"{id}_l", label);
        using (paper.Box($"{id}_w").Width(70).Height(28).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            Origami.NumericField<float>(paper, id, value, setter).Step(0.05f).Show();
    }

    private static void Swap(List<IInputProcessor> list, int a, int b)
    {
        if (b < 0 || b >= list.Count) return;
        (list[a], list[b]) = (list[b], list[a]);
    }

    private static string ProcessorName(IInputProcessor proc) => proc switch
    {
        NormalizeProcessor => "Normalize",
        InvertProcessor => "Invert",
        ScaleProcessor => "Scale",
        ClampProcessor => "Clamp",
        DeadzoneProcessor => "Deadzone",
        ExponentialProcessor => "Exponential",
        _ => proc.GetType().Name
    };

    private static IInputProcessor CreateProcessor(string type) => type switch
    {
        "Deadzone" => new DeadzoneProcessor(0.2f),
        "Scale" => new ScaleProcessor(1f),
        "Invert" => new InvertProcessor(),
        "Clamp" => new ClampProcessor(0f, 1f),
        "Normalize" => new NormalizeProcessor(),
        _ => new ExponentialProcessor(2f)
    };

    private static void DeviceIcon(Paper paper, string id, InputDevice device, float size)
    {
        paper.Box(id).Width(size + 4).Height(size + 4).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Text(InputBindingDisplay.DeviceIcon(device), EditorTheme.DefaultFont!).TextColor(InputBindingDisplay.DeviceColor(device))
            .FontSize(size).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
    }

    // ================================================================
    //  Listening
    // ================================================================

    private bool IsListeningFor(object owner, string? part)
        => InputBindingListener.IsListening && ReferenceEquals(_listenOwner, owner) && _listenPart == part;

    private void StartListening(object owner, string? part, Action<InputBinding> apply)
    {
        bool same = IsListeningFor(owner, part);
        StopListening();
        if (same) return;

        _listenOwner = owner;
        _listenPart = part;
        InputBindingListener.Start(detected =>
        {
            _listenOwner = null;
            apply(detected);
        });
    }

    private void StopListening()
    {
        if (InputBindingListener.IsListening) InputBindingListener.Cancel();
        _listenOwner = null;
        _listenPart = null;
    }
}
