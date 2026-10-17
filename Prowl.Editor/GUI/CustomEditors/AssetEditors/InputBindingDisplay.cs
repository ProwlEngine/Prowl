using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

internal enum InputDevice { Keyboard, Mouse, Gamepad, XR }

/// <summary>What an action hands to code. Drives ActionType and ExpectedValueType together.</summary>
internal enum ActionKind { Button, Axis, Vector2, PassThrough }

/// <summary>A control a binding can point at, as listed in the binding picker.</summary>
internal sealed record InputControlOption(string Label, bool GivesVector, Action<InputBinding> Apply, Func<InputBinding, bool> Matches);

/// <summary>Names, icons, colors and small drawn pieces shared by the input action editor and its binding popup.</summary>
internal static class InputBindingDisplay
{
    public static readonly ActionKind[] Kinds = [ActionKind.Button, ActionKind.Axis, ActionKind.Vector2, ActionKind.PassThrough];
    public static readonly InputDevice[] Devices = [InputDevice.Keyboard, InputDevice.Mouse, InputDevice.Gamepad, InputDevice.XR];
    public static readonly InputInteractionType[] Interactions = Enum.GetValues<InputInteractionType>();

    // ================================================================
    //  Action kind
    // ================================================================

    public static ActionKind GetKind(InputAction action) => action.ActionType switch
    {
        InputActionType.Button => ActionKind.Button,
        InputActionType.PassThrough => ActionKind.PassThrough,
        _ => action.ExpectedValueType == typeof(Float2) ? ActionKind.Vector2 : ActionKind.Axis
    };

    public static void SetKind(InputAction action, ActionKind kind)
    {
        switch (kind)
        {
            case ActionKind.Button:
                action.ActionType = InputActionType.Button;
                action.ExpectedValueType = typeof(float);
                break;
            case ActionKind.Axis:
                action.ActionType = InputActionType.Value;
                action.ExpectedValueType = typeof(float);
                break;
            case ActionKind.Vector2:
                action.ActionType = InputActionType.Value;
                action.ExpectedValueType = typeof(Float2);
                break;
            case ActionKind.PassThrough:
                action.ActionType = InputActionType.PassThrough;
                break;
        }
    }

    public static bool OutputsVector(InputAction action) => action.ExpectedValueType == typeof(Float2);

    public static string KindLabel(ActionKind kind) => kind == ActionKind.PassThrough ? "Pass Through" : kind.ToString();

    public static string KindIcon(ActionKind kind) => kind switch
    {
        ActionKind.Button => EditorIcons.CircleDot,
        ActionKind.Axis => EditorIcons.ArrowsLeftRight,
        ActionKind.Vector2 => EditorIcons.ArrowsUpDownLeftRight,
        _ => EditorIcons.WaveSquare
    };

    public static Color KindColor(ActionKind kind) => kind switch
    {
        ActionKind.Button => EditorTheme.Amber400,
        ActionKind.Axis => EditorTheme.Blue400,
        ActionKind.Vector2 => EditorTheme.Green400,
        _ => EditorTheme.Purple400
    };

    public static string KindDescription(ActionKind kind) => kind switch
    {
        ActionKind.Button => "On or off. Use for jump, fire, interact. Read it with WasPressedThisFrame or IsPressed.",
        ActionKind.Axis => "One number, usually -1 to 1. Use for throttle, zoom, lean. When several bindings move, the one pushed furthest wins.",
        ActionKind.Vector2 => "Two numbers, X and Y. Use for move and look. When several bindings move, the one pushed furthest wins.",
        _ => "A float or Float2 that follows whichever binding moved last, instead of the one pushed furthest. Use for things like a mouse delta."
    };

    public static string CodeLine(InputAction action)
    {
        if (action.ActionType == InputActionType.Button)
            return $"actions[\"{action.Name}\"].WasPressedThisFrame()";
        bool interacts = action.Bindings.Any(b => b.Interaction != InputInteractionType.Default)
            || action.CompositeBindings.Any(c => c.Interaction != InputInteractionType.Default);
        if (interacts)
            return $"actions[\"{action.Name}\"].WasPerformedThisFrame()";
        return $"actions[\"{action.Name}\"].ReadValue<{(OutputsVector(action) ? "Float2" : "float")}>()";
    }

    // ================================================================
    //  Devices and controls
    // ================================================================

    public static InputDevice GetDevice(InputBinding binding) => binding.BindingType switch
    {
        InputBindingType.MouseButton or InputBindingType.MouseAxis => InputDevice.Mouse,
        InputBindingType.GamepadButton or InputBindingType.GamepadAxis or InputBindingType.GamepadTrigger
            or InputBindingType.AnyGamepadButton => InputDevice.Gamepad,
        InputBindingType.XRButton or InputBindingType.XRAxis or InputBindingType.XRStick or InputBindingType.XRTouch => InputDevice.XR,
        _ => InputDevice.Keyboard
    };

    public static InputDevice GetDevice(InputCompositeBinding composite)
        => composite.Parts.Count > 0 ? GetDevice(composite.Parts.Values.First()) : InputDevice.Keyboard;

    public static string DeviceLabel(InputDevice device) => device == InputDevice.XR ? "XR" : device.ToString();

    public static string DeviceIcon(InputDevice device) => device switch
    {
        InputDevice.Mouse => EditorIcons.ComputerMouse,
        InputDevice.Gamepad => EditorIcons.Gamepad,
        InputDevice.XR => EditorIcons.VrCardboard,
        _ => EditorIcons.Keyboard
    };

    public static Color DeviceColor(InputDevice device) => device switch
    {
        InputDevice.Mouse => EditorTheme.Blue600,
        InputDevice.Gamepad => EditorTheme.Green600,
        InputDevice.XR => EditorTheme.Red600,
        _ => EditorTheme.Ink500
    };

    public static bool GivesVector(InputBinding binding)
        => binding.BindingType is InputBindingType.GamepadAxis or InputBindingType.XRStick;

    public static bool GivesVector(InputCompositeBinding composite) => composite is not AxisCompositeBinding;

    public static bool IsAnalog(InputBinding binding) => binding.BindingType is InputBindingType.MouseAxis
        or InputBindingType.GamepadAxis or InputBindingType.GamepadTrigger or InputBindingType.XRAxis or InputBindingType.XRStick;

    public static string ControlName(InputBinding binding)
    {
        string hand = (binding.XRHand ?? XRHand.Right).ToString();
        return binding.BindingType switch
        {
            InputBindingType.Key => binding.Key.HasValue ? KeyLabel(binding.Key.Value) : "None",
            InputBindingType.AnyKey => "Any Key",
            InputBindingType.MouseButton => $"Mouse {binding.MouseButton}",
            InputBindingType.MouseAxis => binding.AxisIndex switch { 0 => "Mouse X", 1 => "Mouse Y", _ => "Wheel" },
            InputBindingType.GamepadButton => binding.GamepadButton?.ToString() ?? "None",
            InputBindingType.AnyGamepadButton => "Any Button",
            InputBindingType.GamepadAxis => binding.AxisIndex == 1 ? "Right Stick" : "Left Stick",
            InputBindingType.GamepadTrigger => binding.AxisIndex == 1 ? "Right Trigger" : "Left Trigger",
            InputBindingType.XRButton => $"{hand} {binding.XRButton}",
            InputBindingType.XRAxis => $"{hand} {binding.XRAxis} Axis",
            InputBindingType.XRStick => $"{hand} Stick",
            InputBindingType.XRTouch => $"{hand} Touch {binding.XRTouch}",
            _ => binding.BindingType.ToString()
        };
    }

    public static string KeyLabel(KeyCode key) => key switch
    {
        >= KeyCode.Number0 and <= KeyCode.Number9 => ((int)key - (int)KeyCode.Number0).ToString(),
        KeyCode.ShiftLeft => "L Shift",
        KeyCode.ShiftRight => "R Shift",
        KeyCode.ControlLeft => "L Ctrl",
        KeyCode.ControlRight => "R Ctrl",
        KeyCode.AltLeft => "L Alt",
        KeyCode.AltRight => "R Alt",
        KeyCode.Escape => "Esc",
        KeyCode.Backspace => "Backspace",
        _ => key.ToString()
    };

    /// <summary>The keys shown before anything is typed into the key filter.</summary>
    public static readonly KeyCode[] CommonKeys =
    [
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F,
        KeyCode.C, KeyCode.V, KeyCode.X, KeyCode.Z, KeyCode.G, KeyCode.T,
        KeyCode.Space, KeyCode.ShiftLeft, KeyCode.ControlLeft, KeyCode.AltLeft, KeyCode.Tab,
        KeyCode.Escape, KeyCode.Enter, KeyCode.Backspace,
        KeyCode.Up, KeyCode.Down, KeyCode.Left, KeyCode.Right,
        KeyCode.Number1, KeyCode.Number2, KeyCode.Number3, KeyCode.Number4, KeyCode.Number5,
    ];

    public static readonly KeyCode[] AllKeys = [.. Enum.GetValues<KeyCode>().Where(k => k != KeyCode.Unknown).Distinct()];

    public static string CompositeName(InputCompositeBinding composite) => composite switch
    {
        Vector2CompositeBinding => "Four directions",
        DualAxisCompositeBinding => "Dual axis",
        AxisCompositeBinding => "Two key axis",
        _ => "Composite"
    };

    public static string PartName(string part) => part switch
    {
        Vector2CompositeBinding.UP => "Up",
        Vector2CompositeBinding.DOWN => "Down",
        Vector2CompositeBinding.LEFT => "Left",
        Vector2CompositeBinding.RIGHT => "Right",
        AxisCompositeBinding.POSITIVE => "Positive",
        AxisCompositeBinding.NEGATIVE => "Negative",
        DualAxisCompositeBinding.X_AXIS => "X",
        DualAxisCompositeBinding.Y_AXIS => "Y",
        _ => part
    };

    /// <summary>A short warning when a binding hands the action the wrong value type, or null.</summary>
    public static string? Mismatch(InputAction action, bool givesVector, bool isComposite)
    {
        bool wantsVector = OutputsVector(action);
        if (givesVector && !wantsVector) return "Gives Float2";
        if (!givesVector && wantsVector) return isComposite ? "Gives float" : "Needs a composite";
        return null;
    }

    /// <summary>Points a binding at a new control type, clearing what the old type used.</summary>
    public static void SetControlType(InputBinding binding, InputBindingType type)
    {
        int? device = binding.RequiredDeviceIndex;
        XRHand hand = binding.XRHand ?? XRHand.Right;
        binding.ClearControl();
        binding.BindingType = type;

        InputDevice kind = GetDevice(binding);
        binding.RequiredDeviceIndex = kind == InputDevice.Gamepad ? device : null;
        if (kind == InputDevice.XR) binding.XRHand = hand;
    }

    /// <summary>Copies another binding's control, leaving interaction and processors alone. Gamepads read from any pad.</summary>
    public static void CopyControl(InputBinding target, InputBinding source)
    {
        target.ClearControl();
        target.BindingType = source.BindingType;
        target.Key = source.Key;
        target.MouseButton = source.MouseButton;
        target.GamepadButton = source.GamepadButton;
        target.AxisIndex = source.AxisIndex;
        target.XRHand = source.XRHand;
        target.XRButton = source.XRButton;
        target.XRAxis = source.XRAxis;
        target.XRTouch = source.XRTouch;
        target.RequiredDeviceIndex = null;
    }

    public static InputBinding CreateDefault(InputDevice device, bool wantsVector) => device switch
    {
        InputDevice.Mouse => wantsVector
            ? new InputBinding { BindingType = InputBindingType.MouseAxis, AxisIndex = 0 }
            : InputBinding.CreateMouseButtonBinding(MouseButton.Left),
        InputDevice.Gamepad => wantsVector
            ? new InputBinding { BindingType = InputBindingType.GamepadAxis, AxisIndex = 0, Processors = [new DeadzoneProcessor(0.2f)] }
            : new InputBinding { BindingType = InputBindingType.GamepadButton, GamepadButton = GamepadButton.A },
        InputDevice.XR => wantsVector
            ? InputBinding.CreateXRStickBinding(XRHand.Right)
            : InputBinding.CreateXRButtonBinding(XRHand.Right, XRButton.Primary),
        _ => InputBinding.CreateKeyBinding(KeyCode.Space)
    };

    /// <summary>The pickable controls for a device. Keyboard keys are picked from a searchable list instead.</summary>
    public static List<InputControlOption> Options(InputDevice device)
    {
        var list = new List<InputControlOption>();
        switch (device)
        {
            case InputDevice.Keyboard:
                list.Add(new("Any Key", false, b => SetControlType(b, InputBindingType.AnyKey), b => b.BindingType == InputBindingType.AnyKey));
                break;

            case InputDevice.Mouse:
                foreach (MouseButton button in Enum.GetValues<MouseButton>().Where(b => b >= MouseButton.Left && b <= MouseButton.Button5))
                    list.Add(new(button.ToString(), false,
                        b => { SetControlType(b, InputBindingType.MouseButton); b.MouseButton = button; },
                        b => b.BindingType == InputBindingType.MouseButton && b.MouseButton == button));
                string[] axes = ["Mouse X", "Mouse Y", "Wheel"];
                for (int i = 0; i < axes.Length; i++)
                {
                    int axis = i;
                    list.Add(new(axes[i], false,
                        b => { SetControlType(b, InputBindingType.MouseAxis); b.AxisIndex = axis; },
                        b => b.BindingType == InputBindingType.MouseAxis && b.AxisIndex == axis));
                }
                break;

            case InputDevice.Gamepad:
                list.Add(new("Any Button", false, b => SetControlType(b, InputBindingType.AnyGamepadButton),
                    b => b.BindingType == InputBindingType.AnyGamepadButton));
                foreach (GamepadButton button in Enum.GetValues<GamepadButton>().Where(b => b != GamepadButton.Unknown))
                    list.Add(new(button.ToString(), false,
                        b => { SetControlType(b, InputBindingType.GamepadButton); b.GamepadButton = button; },
                        b => b.BindingType == InputBindingType.GamepadButton && b.GamepadButton == button));
                for (int i = 0; i < 2; i++)
                {
                    int side = i;
                    string name = side == 0 ? "Left" : "Right";
                    list.Add(new($"{name} Stick", true,
                        b => { SetControlType(b, InputBindingType.GamepadAxis); b.AxisIndex = side; },
                        b => b.BindingType == InputBindingType.GamepadAxis && b.AxisIndex == side));
                    list.Add(new($"{name} Trigger", false,
                        b => { SetControlType(b, InputBindingType.GamepadTrigger); b.AxisIndex = side; },
                        b => b.BindingType == InputBindingType.GamepadTrigger && b.AxisIndex == side));
                }
                break;

            case InputDevice.XR:
                foreach (XRButton button in Enum.GetValues<XRButton>())
                    list.Add(new(button.ToString(), false,
                        b => { SetControlType(b, InputBindingType.XRButton); b.XRButton = button; },
                        b => b.BindingType == InputBindingType.XRButton && b.XRButton == button));
                foreach (XRAxis axis in Enum.GetValues<XRAxis>())
                    list.Add(new($"{axis} Axis", false,
                        b => { SetControlType(b, InputBindingType.XRAxis); b.XRAxis = axis; },
                        b => b.BindingType == InputBindingType.XRAxis && b.XRAxis == axis));
                list.Add(new("Stick", true, b => SetControlType(b, InputBindingType.XRStick), b => b.BindingType == InputBindingType.XRStick));
                foreach (XRTouch touch in Enum.GetValues<XRTouch>())
                    list.Add(new($"Touch {touch}", false,
                        b => { SetControlType(b, InputBindingType.XRTouch); b.XRTouch = touch; },
                        b => b.BindingType == InputBindingType.XRTouch && b.XRTouch == touch));
                break;
        }
        return list;
    }

    // ================================================================
    //  Interactions
    // ================================================================

    public static string InteractionName(InputInteractionType type) => type switch
    {
        InputInteractionType.MultiTap => "Multi tap",
        InputInteractionType.PressAndRelease => "Press and release",
        _ => type.ToString()
    };

    public static string InteractionDescription(InputInteractionType type) => type switch
    {
        InputInteractionType.Default => "Performs on press and keeps performing while held.",
        InputInteractionType.Press => "Performs once on the frame it goes down.",
        InputInteractionType.Release => "Performs once when let go.",
        InputInteractionType.Hold => "Performs once it has been held long enough.",
        InputInteractionType.Tap => "Performs on a quick press and release, not when held too long.",
        InputInteractionType.MultiTap => "Performs after several quick taps in a row.",
        _ => "Performs on press and again on release."
    };

    public static string InteractionSummary(IInputInteractionSettings settings) => settings.Interaction switch
    {
        InputInteractionType.Hold => $"Hold {settings.HoldDuration:0.##}s",
        InputInteractionType.MultiTap => $"{settings.TapCount}x tap",
        _ => InteractionName(settings.Interaction)
    };


    // ================================================================
    //  Drawn pieces
    // ================================================================

    public static float LabelSize => EditorTheme.FontSizeSmall - 2;

    /// <summary>A key cap style label for one control. A fixed width lines several caps up into a grid.</summary>
    public static void Keycap(Paper paper, string id, string text, Color color, bool large = false, float? width = null)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        ElementBuilder box = paper.Box(id).Height(large ? 32 : 22)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Ink300, 26))
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .Text(text, EditorTheme.FontMono ?? EditorTheme.DefaultFont!).TextColor(color)
            .FontSize(large ? EditorTheme.FontSizeSmall : LabelSize).Alignment(TextAlignment.MiddleCenter)
            .IsNotInteractable();

        if (width.HasValue) box.Width(width.Value);
        else box.Width(UnitValue.Auto).MinWidth(large ? 32 : 22).Padding(large ? 10 : 7, large ? 10 : 7, 0, 0);
    }

    /// <summary>The width a key cap needs for the widest of several labels, so they can share one size.</summary>
    public static float KeycapWidth(Paper paper, IEnumerable<string> labels)
    {
        Scribe.FontFile font = EditorTheme.FontMono ?? EditorTheme.DefaultFont!;
        float widest = labels.Select(l => (float)paper.MeasureText(l, LabelSize, font).X).DefaultIfEmpty(0f).Max();
        return MathF.Max(22f, widest + 14f);
    }

    /// <summary>A clickable key cap, for picking a control from a grid.</summary>
    public static void KeyButton(Paper paper, string id, string text, Color color, bool selected, bool enabled, Action onClick, string? tooltip = null)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        ElementBuilder box = paper.Box(id).Width(UnitValue.Auto).MinWidth(30).Height(28).Padding(9, 9, 0, 0)
            .Rounded(m.SmallRounding)
            .BackgroundColor(selected ? EditorTheme.Accent : EditorTheme.WithAlpha(EditorTheme.Ink300, 26))
            .BorderColor(selected ? EditorTheme.Accent : EditorTheme.BorderStrong).BorderWidth(1)
            .Text(text, EditorTheme.FontMono ?? EditorTheme.DefaultFont!).TextColor(selected ? Color.White : color)
            .FontSize(LabelSize).Alignment(TextAlignment.MiddleCenter);

        if (enabled)
            box.Hovered.BorderColor(EditorTheme.AccentBright).End().Cursor(PaperCursor.Pointer).OnClick(0, (_, _) => onClick());
        else
            box.Opacity(0.35f);

        if (tooltip != null) box.Tooltip(tooltip);
    }

    /// <summary>A small text button that sizes to its label.</summary>
    public static void SmallButton(Paper paper, string id, string text, Action onClick, Color? textColor = null,
        bool primary = false, bool disabled = false, string? tooltip = null)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        ElementBuilder box = paper.Box(id).Width(UnitValue.Auto).Height(28).Padding(11, 11, 0, 0)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(m.Rounding)
            .BackgroundColor(primary ? EditorTheme.Accent : Color.Transparent)
            .BorderColor(primary ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1)
            .Text(text, EditorTheme.FontMedium ?? EditorTheme.DefaultFont!)
            .TextColor(primary ? Color.White : textColor ?? EditorTheme.Ink400)
            .FontSize(LabelSize + 1).Alignment(TextAlignment.MiddleCenter);

        if (disabled)
            box.Opacity(0.35f);
        else
            box.Hovered.BackgroundColor(primary ? EditorTheme.AccentBright : EditorTheme.WithAlpha(EditorTheme.Ink300, 22)).End()
                .Cursor(PaperCursor.Pointer).OnClick(0, (_, _) => onClick());

        if (tooltip != null) box.Tooltip(tooltip);
    }

    /// <summary>A square icon button.</summary>
    public static void IconBox(Paper paper, string id, string glyph, Action onClick, bool disabled = false, string? tooltip = null)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        ElementBuilder box = paper.Box(id).Width(26).Height(26).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Rounded(m.SmallRounding)
            .Text(glyph, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink400)
            .FontSize(LabelSize).Alignment(TextAlignment.MiddleCenter);

        if (disabled)
            box.Opacity(0.3f);
        else
            box.Hovered.BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Ink300, 26)).TextColor(EditorTheme.Ink500).End()
                .Cursor(PaperCursor.Pointer).OnClick(0, (_, _) => onClick());

        if (tooltip != null) box.Tooltip(tooltip);
    }

    /// <summary>One segment of a <see cref="Segmented"/> control.</summary>
    public readonly record struct Segment(string Label, string? Subtitle = null, string? Tooltip = null);

    /// <summary>A row of equal width choices with the picked one raised, each with an optional subtitle under it.</summary>
    public static void Segmented(Paper paper, string id, IReadOnlyList<Segment> segments, int selected, Action<int> onSelect)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        bool tall = segments.Any(s => s.Subtitle != null);

        using (paper.Row(id).Height(tall ? 50 : 34).Padding(3, 3, 3, 3).Gap(3)
            .Rounded(m.Rounding + 2).BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                bool on = i == selected;

                ElementBuilder cell = paper.Column($"{id}_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                    .Rounded(m.Rounding)
                    .BackgroundColor(on ? EditorTheme.Neutral400 : Color.Transparent)
                    .BorderColor(on ? EditorTheme.BorderStrong : Color.Transparent).BorderWidth(1)
                    .Hovered.BackgroundColor(on ? EditorTheme.Neutral400 : EditorTheme.WithAlpha(EditorTheme.Ink300, 16)).End()
                    .Cursor(PaperCursor.Pointer)
                    .OnClick(i, (index, _) => onSelect(index));
                if (segment.Tooltip != null) cell.Tooltip(segment.Tooltip);

                using (cell.Enter())
                {
                    paper.Box($"{id}_{i}_l").Height(UnitValue.Stretch())
                        .Text(segment.Label, (on ? EditorTheme.FontSemiBold : EditorTheme.FontMedium) ?? EditorTheme.DefaultFont!)
                        .TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink400)
                        .FontSize(LabelSize + 1)
                        .Alignment(segment.Subtitle != null ? TextAlignment.BottomCenter : TextAlignment.MiddleCenter)
                        .IsNotInteractable();

                    if (segment.Subtitle != null)
                        paper.Box($"{id}_{i}_s").Height(UnitValue.Stretch()).Margin(0, 0, 2, 0)
                            .Text(segment.Subtitle, EditorTheme.FontMono ?? EditorTheme.DefaultFont!)
                            .TextColor(on ? EditorTheme.AccentText : EditorTheme.Ink300)
                            .FontSize(LabelSize - 2).Alignment(TextAlignment.Center)
                            .IsNotInteractable();
                }
            }
        }
    }

    /// <summary>A small tinted pill for a state or setting.</summary>
    public static ElementBuilder Tag(Paper paper, string id, string text, Color color)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        return paper.Box(id).Width(UnitValue.Auto).Height(22).Padding(8, 8, 0, 0)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(m.SmallRounding)
            .BackgroundColor(EditorTheme.WithAlpha(color, 36))
            .Text(text, EditorTheme.FontMedium ?? EditorTheme.DefaultFont!).TextColor(color)
            .FontSize(LabelSize).Alignment(TextAlignment.MiddleCenter);
    }

    /// <summary>A composite drawn the way it reads on the keyboard: a D-pad, a negative and positive pair, or X and Y.</summary>
    public static void CompositeSummary(Paper paper, string id, InputCompositeBinding composite)
    {
        Color color = DeviceColor(GetDevice(composite));
        string Part(string name) => composite.Parts.TryGetValue(name, out InputBinding? p) ? ControlName(p) : "?";

        if (composite is Vector2CompositeBinding)
        {
            string up = Part(Vector2CompositeBinding.UP), left = Part(Vector2CompositeBinding.LEFT);
            string down = Part(Vector2CompositeBinding.DOWN), right = Part(Vector2CompositeBinding.RIGHT);
            float cell = KeycapWidth(paper, [up, left, down, right]);

            using (paper.Column(id).Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(2)
                .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable().Enter())
            {
                using (paper.Row($"{id}_top").Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(2).Enter())
                {
                    paper.Box($"{id}_tl").Width(cell).Height(1);
                    Keycap(paper, $"{id}_u", up, color, width: cell);
                    paper.Box($"{id}_tr").Width(cell).Height(1);
                }
                using (paper.Row($"{id}_bot").Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(2).Enter())
                {
                    Keycap(paper, $"{id}_l", left, color, width: cell);
                    Keycap(paper, $"{id}_d", down, color, width: cell);
                    Keycap(paper, $"{id}_r", right, color, width: cell);
                }
            }
            return;
        }

        (string a, string b, string la, string lb) = composite is DualAxisCompositeBinding
            ? ("X", "Y", Part(DualAxisCompositeBinding.X_AXIS), Part(DualAxisCompositeBinding.Y_AXIS))
            : ("-", "+", Part(AxisCompositeBinding.NEGATIVE), Part(AxisCompositeBinding.POSITIVE));

        using (paper.Row(id).Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(5)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable().Enter())
        {
            Caption(paper, $"{id}_a", a);
            Keycap(paper, $"{id}_ka", la, color);
            Caption(paper, $"{id}_b", b);
            Keycap(paper, $"{id}_kb", lb, color);
        }
    }

    /// <summary>A short line of muted text that sizes to its content.</summary>
    public static void Caption(Paper paper, string id, string text)
    {
        paper.Box(id).Width(UnitValue.Auto).Height(22).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Text(text, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink300)
            .FontSize(LabelSize + 1).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
    }

    /// <summary>An uppercase field heading with an optional hint on the right.</summary>
    public static void FieldHeading(Paper paper, string id, string text, string? hint = null)
    {
        using (paper.Row(id).Height(20).Gap(6).Margin(0, 0, 4, 0).IsNotInteractable().Enter())
        {
            paper.Box($"{id}_t").Width(UnitValue.Auto).Height(20)
                .Text(text.ToUpperInvariant(), EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink300)
                .FontSize(LabelSize).LetterSpacing(0.8f).Alignment(TextAlignment.MiddleLeft);
            if (hint != null)
                paper.Box($"{id}_h").Height(20)
                    .Text(hint, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink300)
                    .FontSize(LabelSize).Alignment(TextAlignment.MiddleRight);
        }
    }

    /// <summary>A wrapping line of quiet explanatory text.</summary>
    public static void Hint(Paper paper, string id, string text, Color? color = null)
    {
        paper.Box(id).Height(UnitValue.Auto).MinHeight(20)
            .Text(text, EditorTheme.DefaultFont!).TextColor(color ?? EditorTheme.Ink300)
            .FontSize(LabelSize + 1).Wrap(Scribe.TextWrapMode.Wrap)
            .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
    }
}
