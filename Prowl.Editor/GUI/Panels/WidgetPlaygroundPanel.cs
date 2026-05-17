using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.Editor.Inspector;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;
namespace Prowl.Editor.GUI.Panels;

[EditorWindow("Debug/Widget Playground")]
public class WidgetPlaygroundPanel : DockPanel
{
    public override string Title => "Widget Playground";
    public override string Icon => EditorIcons.Flask;

    private bool _toggleA = true;
    private bool _toggleB;
    private bool _toggleBtnA;
    private bool _toggleBtnB = true;
    private string _textValue = "Hello World";
    private string _nameValue = "Player One";
    private float _floatValue = 3.14f;
    private int _intValue = 42;
    private float _sliderValue = 0.5f;
    private float _sliderRange = 75f;
    private int _intSlider = 5;
    private int _dropdownIndex;
    private int _dropdown2Index = 2;
    private string _searchText = "";
    private int _clickCount;
    private Vector.Float2 _vec2 = new(1.5f, 2.5f);
    private Vector.Float3 _vec3 = new(10f, 20f, 30f);
    private Vector.Color _color = new(0.2f, 0.6f, 1f, 1f);
    private float _progress = 0.45f;
    private TestEnum _testEnum = TestEnum.Option2;

    public enum TestEnum { Option1, Option2, Option3, SuperLongOptionName }

    private Runtime.AnimationCurve _curve = new();
    private TestComponent _testObject = new();

    // Test class for property grid exercises every type the grid supports
    public class TestComponent
    {
        // Primitives
        public string Name = "Player";
        public bool IsActive = true;
        public int Health = 100;
        public float Speed = 5.5f;
        public double Precision = 3.14159265358979;
        public byte Opacity = 200;
        public long BigNumber = 999999999L;
        public uint Flags = 42u;

        // Enum
        public TestEnum Mode = TestEnum.Option2;

        // Math types
        public Vector.Float2 UV = new(0.5f, 0.75f);
        public Vector.Float3 Position = new(1, 2, 3);
        public Vector.Float4 Custom = new(0.1f, 0.2f, 0.3f, 0.4f);
        public Vector.Color Tint = new(0.5f, 0.8f, 1f, 1f);
        public Vector.Quaternion Rotation = Prowl.Vector.Quaternion.Identity;

        // Guid (read-only)
        public System.Guid Id = System.Guid.NewGuid();

        // AnimationCurve
        public Runtime.AnimationCurve SpeedCurve = new();

        // Collections
        public List<float> Scores = new() { 10.5f, 20.3f, 30.1f };
        public List<string> Tags = new() { "Player", "Friendly" };
        public int[] LevelData = new int[] { 1, 5, 10, 25, 50 };
        public List<Vector.Float3> Waypoints = new()
        {
            new(0, 0, 0), new(10, 0, 5), new(20, 0, 0)
        };

        // Dictionary
        public Dictionary<string, float> Stats = new()
        {
            { "Attack", 15f }, { "Defense", 10f }, { "Speed", 8f }
        };

        // Nested objects
        public PhysicsSettings Physics = new();
        public RenderSettings? Rendering = new();
        public BaseAbility? Ability = null; // Polymorphism test (null, abstract)

        // Nested list of objects
        public List<StatusEffect> Effects = new()
        {
            new() { Name = "Burn", Duration = 5f, DamagePerSecond = 2.5f },
            new() { Name = "Slow", Duration = 3f, DamagePerSecond = 0f }
        };
    }

    public class PhysicsSettings
    {
        public float Gravity = 9.81f;
        public bool EnablePhysics = true;
        public float Mass = 1.0f;
        public float Drag = 0.1f;
        public Vector.Float3 Velocity = new(0, 0, 0);
    }

    public class RenderSettings
    {
        public bool CastShadows = true;
        public bool ReceiveShadows = true;
        public float LODBias = 1.0f;
        public Vector.Color EmissionColor = new(0, 0, 0, 1);
    }

    public abstract class BaseAbility
    {
        public string AbilityName = "Unknown";
        public float Cooldown = 1f;
    }

    public class FireballAbility : BaseAbility
    {
        public float Damage = 50f;
        public float Range = 10f;
        public Vector.Color FlameColor = new(1f, 0.5f, 0f, 1f);

        public FireballAbility() { AbilityName = "Fireball"; Cooldown = 2.5f; }
    }

    public class HealAbility : BaseAbility
    {
        public float HealAmount = 30f;
        public bool AffectsAllies = true;

        public HealAbility() { AbilityName = "Heal"; Cooldown = 5f; }
    }

    public class StatusEffect
    {
        public string Name = "";
        public float Duration = 0f;
        public float DamagePerSecond = 0f;
        public bool IsDebuff = true;
    }

    private static readonly string[] Fruits = { "Apple", "Banana", "Cherry", "Date", "Elderberry" };
    private static readonly string[] Modes = { "Constant", "Curve", "Random Between Two" };

    public override void OnGUI(Paper paper, float width, float height)
    {
        Origami.ScrollView(paper, "playground_scroll", width, height)
            .Padding(8, 8, 8, 0)
            .ColSpacing(4)
            .Body(() =>
        {
            // === Color Field (at top for testing) ===
            Origami.Header(paper, "h_col_top", "Color Field").Show();
            InspectorRow.Draw(paper, "cf_top", "Tint", () =>
                Origami.ColorField(paper, "cf_top_cf", _color, v => _color = v).Show());

            Origami.Separator(paper, "sep_col_top").Show();

            // === Raw Paper TextField test ===
            Origami.Header(paper, "h_raw", "Raw Paper TextField").Show();
            paper.Box("raw_tf_bare")
                .Height(EditorTheme.RowHeight)
                .Width(Prowl.PaperUI.LayoutEngine.UnitValue.Stretch())
                .FontSize(EditorTheme.FontSize)
                .TextField(_textValue, EditorTheme.DefaultFont!,
                    onChange: v => _textValue = v,
                    textColor: EditorTheme.Ink500);

            Origami.Separator(paper, "sep_raw").Show();

            // === Buttons ===
            Origami.Header(paper, "h_btn", "Buttons").Show();

            using (paper.Row("btn_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                Origami.Button(paper, "btn1", "Click Me", () => _clickCount++).Show();
                Origami.Button(paper, "btn2", "Reset", () => ResetAll()).Show();
                Origami.Button(paper, "btn3", "Wide Button").Width(160).Show();
            }
            Origami.Label(paper, "btn_count", $"Click count: {_clickCount}").Show();

            Origami.Separator(paper, "sep1").Show();

            // === Toggle switches ===
            Origami.Header(paper, "h_togbtn", "Toggles").Show();

            using (paper.Row("togbtn_row").Height(EditorTheme.RowHeight).RowBetween(12).Enter())
            {
                Origami.Switch(paper, "tbtn_a", _toggleBtnA, v => _toggleBtnA = v)
                    .Primary().LabelRight("Wireframe").Show();
                Origami.Switch(paper, "tbtn_b", _toggleBtnB, v => _toggleBtnB = v)
                    .Primary().LabelRight("Grid").Show();
            }

            Origami.Separator(paper, "sep1b").Show();

            // === Toggles ===
            Origami.Header(paper, "h_tog", "Toggles").Show();

            Origami.Checkbox(paper, "tog_a", _toggleA, v => _toggleA = v).LabelRight("Enable Shadows").Show();
            Origami.Checkbox(paper, "tog_b", _toggleB, v => _toggleB = v).LabelRight("Cast Reflections").Show();

            Origami.Separator(paper, "sep2").Show();

            // === Text Fields ===
            Origami.Header(paper, "h_txt", "Text Fields").Show();

            InspectorRow.Draw(paper, "tf_text", "Message", () =>
                Origami.TextField(paper, "tf_text_v", _textValue, v => _textValue = v).Show());
            InspectorRow.Draw(paper, "tf_name", "Name", () =>
                Origami.TextField(paper, "tf_name_v", _nameValue, v => _nameValue = v).Show());

            Origami.Separator(paper, "sep3").Show();

            // === Numeric Fields ===
            Origami.Header(paper, "h_num", "Numeric Fields").Show();

            InspectorRow.Draw(paper, "ff_float", "Speed", () =>
                Origami.NumericField<float>(paper, "ff_float_v", _floatValue, v => _floatValue = v).Show());
            InspectorRow.Draw(paper, "if_int", "Health", () =>
                Origami.NumericField<int>(paper, "if_int_v", _intValue, v => _intValue = v).Show());

            Origami.Separator(paper, "sep4").Show();

            // === Sliders ===
            Origami.Header(paper, "h_sl", "Sliders").Show();

            InspectorRow.Draw(paper, "sl_norm", "Opacity", () =>
                Origami.Slider(paper, "sl_norm_v", _sliderValue, v => _sliderValue = v, 0f, 1f)
                    .Format("F2").Show());
            InspectorRow.Draw(paper, "sl_range", "Volume", () =>
                Origami.Slider(paper, "sl_range_v", _sliderRange, v => _sliderRange = v, 0f, 100f)
                    .Format("F1").Show());

            Origami.Separator(paper, "sep5").Show();

            // === Foldouts ===
            Origami.Header(paper, "h_fold", "Foldouts").Show();

            Origami.Foldout(paper, "fo_1", "Advanced Settings").Body(() =>
            {
                using (paper.Column("fo_1_c").Height(UnitValue.Auto).ChildLeft(16).RowBetween(4).Enter())
                {
                    InspectorRow.Draw(paper, "fo_speed", "Speed", () =>
                        Origami.NumericField<float>(paper, "fo_speed_v", _floatValue, v => _floatValue = v).Show());
                    Origami.Checkbox(paper, "fo_tog", _toggleA, v => _toggleA = v).LabelRight("Enabled").Show();
                }
            });

            Origami.Foldout(paper, "fo_2", "Debug Options").Body(() =>
            {
                using (paper.Column("fo_2_c").Height(UnitValue.Auto).ChildLeft(16).RowBetween(4).Enter())
                {
                    Origami.Checkbox(paper, "fo_dbg", _toggleB, v => _toggleB = v).LabelRight("Show Wireframe").Show();
                    InspectorRow.Draw(paper, "fo_iter", "Iterations", () =>
                        Origami.NumericField<int>(paper, "fo_iter_v", _intValue, v => _intValue = v).Show());
                }
            });

            Origami.Separator(paper, "sep6").Show();

            // === Dropdowns ===
            Origami.Header(paper, "h_dd", "Dropdowns").Show();

            InspectorRow.Draw(paper, "dd_fruit", "Fruit", () =>
                Origami.Dropdown(paper, "dd_fruit_v", _dropdownIndex, v => _dropdownIndex = v, Fruits).Show());
            InspectorRow.Draw(paper, "dd_mode", "Mode", () =>
                Origami.Dropdown(paper, "dd_mode_v", _dropdown2Index, v => _dropdown2Index = v, Modes).Show());

            Origami.Separator(paper, "sep7").Show();

            // === Search Bar ===
            Origami.Header(paper, "h_search", "Search Bar").Show();

            Origami.SearchField(paper, "sb_1", _searchText, v => _searchText = v, "Type to search...").Show();

            Origami.Separator(paper, "sep8").Show();

            // === Enum Dropdown ===
            Origami.Header(paper, "h_enum", "Enum Dropdown").Show();

            InspectorRow.Draw(paper, "dd_enum", "Test Enum", () =>
                Origami.EnumDropdown(paper, "dd_enum_v", _testEnum, v => _testEnum = v).Show());

            Origami.Separator(paper, "sep8b").Show();

            // === Int Slider ===
            Origami.Header(paper, "h_isl", "Int Slider").Show();

            InspectorRow.Draw(paper, "isl_1", "Count", () =>
                Origami.IntSlider(paper, "isl_1_v", _intSlider, v => _intSlider = v, 0, 20).Show());

            Origami.Separator(paper, "sep8c").Show();

            // === Vector Fields ===
            Origami.Header(paper, "h_vec", "Vector Fields").Show();

            InspectorRow.Draw(paper, "v2_1", "Position 2D", () =>
                Origami.Float2Field(paper, "v2_1_vf", _vec2, v => _vec2 = v).Show());
            InspectorRow.Draw(paper, "v3_1", "Position 3D", () =>
                Origami.Float3Field(paper, "v3_1_vf", _vec3, v => _vec3 = v).Show());

            Origami.Separator(paper, "sep8d").Show();

            // === Color Field ===
            Origami.Header(paper, "h_col", "Color Field").Show();

            InspectorRow.Draw(paper, "cf_1", "Tint", () =>
                Origami.ColorField(paper, "cf_1_cf", _color, v => _color = v).Show());

            Origami.Separator(paper, "sep8e").Show();

            // === Progress Bar ===
            Origami.Header(paper, "h_prog", "Progress Bar").Show();

            Origami.ProgressBar(paper, "pb_1", _progress).Label("Loading").ShowPercent().Show();
            InspectorRow.Draw(paper, "pb_ctrl", "Progress", () =>
                Origami.Slider(paper, "pb_ctrl_v", _progress, v => _progress = v, 0f, 1f)
                    .Format("F2").Show());

            Origami.Separator(paper, "sep9").Show();

            // === Context Menu ===
            Origami.Header(paper, "h_ctx", "Context Menu (Right-Click)").Show();

            using (paper.Box("ctx_demo_area")
                .Height(60)
                .BackgroundColor(EditorTheme.Neutral300)
                .Rounded(6)
                .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .ChildLeft(16).ChildTop(8)
                .Enter())
            {
                if (EditorTheme.DefaultFont != null)
                    paper.Box("ctx_hint")
                        .IsNotInteractable()
                        .Text("Right-click here for a context menu", EditorTheme.DefaultFont)
                        .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize);

                Origami.RightClickMenu(paper, "ctx_test", menu =>
                {
                    menu.Item("Cut", () => _textValue = "Cut!")
                        .Item("Copy", () => _textValue = "Copy!")
                        .Item("Paste", () => _textValue = "Paste!")
                        .Separator()
                        .Item("Delete", () => _textValue = "Deleted!", enabled: _toggleA)
                        .Separator()
                        .Submenu("More Options", sub =>
                        {
                            sub.Item("Option A", () => _textValue = "Option A");
                            sub.Item("Option B", () => _textValue = "Option B");
                        });
                });
            }

            Origami.Separator(paper, "sep10").Show();

            // === Modal Dialog ===
            Origami.Header(paper, "h_modal", "Modal Dialog").Show();

            using (paper.Row("modal_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                Origami.Button(paper, "btn_confirm", "Confirm Dialog", () => Origami.Confirm("Delete Object",
                    "Are you sure you want to delete this object?",
                    () => Toasts.Success("Deleted", "Object was deleted"),
                    () => Toasts.Info("Cancelled", "Deletion cancelled"))).Show();

                Origami.Button(paper, "btn_message", "Message Dialog", () => Origami.Message("Info", "This is a message dialog.")).Show();
            }

            Origami.Separator(paper, "sep11").Show();

            // === Toasts ===
            Origami.Header(paper, "h_toast", "Toast Notifications").Show();

            using (paper.Row("toast_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                Origami.Button(paper, "btn_toast_info", "Info", () => Toasts.Info("Info", "Something happened")).Show();
                Origami.Button(paper, "btn_toast_ok", "Success", () => Toasts.Success("Saved", "Scene saved successfully")).Show();
                Origami.Button(paper, "btn_toast_warn", "Warning", () => Toasts.Warning("Warning", "Asset may be outdated")).Show();
                Origami.Button(paper, "btn_toast_err", "Error", () => Toasts.Error("Error", "Failed to compile shader")).Show();
            }

            Origami.Separator(paper, "sep12").Show();

            // === Tooltip ===
            Origami.Header(paper, "h_tooltip", "Tooltip (hover the button)").Show();

            var tooltipBtn = paper.Box("tooltip_demo")
                .Height(EditorTheme.RowHeight)
                .Width(200)
                .BackgroundColor(EditorTheme.Ink100)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(3)
                .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .Tooltip("This is a tooltip! It appears after a short hover delay.");
            if (EditorTheme.DefaultFont != null)
                tooltipBtn.Text("Hover me for tooltip", EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            Origami.Separator(paper, "sep13").Show();

            // === Animation Curve ===
            Origami.Header(paper, "h_curve", "Animation Curve").Show();

            InspectorRow.Draw(paper, "curve_1", "Speed Curve", () =>
                CurveField.Create(paper, "curve_1_cf", _curve,
                    v => _curve = v).Show());

            Origami.Separator(paper, "sep14").Show();

            // === Property Grid ===
            Origami.Header(paper, "h_propgrid", "Property Grid (Reflection)").Show();

            PropertyGridUtils.Draw(paper, "pg_test", _testObject, changed => _testObject = (TestComponent)changed);

            Origami.Separator(paper, "sep15").Show();

            // === Live State ===
            Origami.Header(paper, "h_state", "Current State").Show();
            Origami.Label(paper, "st_click", $"Clicks: {_clickCount}").Show();
            Origami.Label(paper, "st_togbtn", $"Wireframe: {_toggleBtnA}  Grid: {_toggleBtnB}").Show();
            Origami.Label(paper, "st_tog", $"Shadows: {_toggleA}  Reflections: {_toggleB}").Show();
            Origami.Label(paper, "st_text", $"Message: {_textValue}").Show();
            Origami.Label(paper, "st_name", $"Name: {_nameValue}").Show();
            Origami.Label(paper, "st_float", $"Speed: {_floatValue:F2}").Show();
            Origami.Label(paper, "st_int", $"Health: {_intValue}").Show();
            Origami.Label(paper, "st_sl1", $"Opacity: {_sliderValue:F2}").Show();
            Origami.Label(paper, "st_sl2", $"Volume: {_sliderRange:F1}").Show();
            Origami.Label(paper, "st_dd1", $"Fruit: {Fruits[_dropdownIndex]}").Show();
            Origami.Label(paper, "st_dd2", $"Mode: {Modes[_dropdown2Index]}").Show();
            Origami.Label(paper, "st_search", $"Search: \"{_searchText}\"").Show();
            Origami.Label(paper, "st_enum", $"Enum: {_testEnum}").Show();
            Origami.Label(paper, "st_isl", $"IntSlider: {_intSlider}").Show();
            Origami.Label(paper, "st_vec2", $"Vec2: ({_vec2.X:F1}, {_vec2.Y:F1})").Show();
            Origami.Label(paper, "st_vec3", $"Vec3: ({_vec3.X:F1}, {_vec3.Y:F1}, {_vec3.Z:F1})").Show();
            Origami.Label(paper, "st_col", $"Color: R={_color.R:F2} G={_color.G:F2} B={_color.B:F2}").Show();
            Origami.Label(paper, "st_prog", $"Progress: {_progress:P0}").Show();

            // ── File Dialog ──
            Origami.Header(paper, "h_filedialog", "File Dialog").Show();

            Origami.Button(paper, "btn_open_file", "Open File...", () => EditorApplication.OpenFileDialog(FileDialogMode.Open,
                path => { if (path != null) Toasts.Show("File", $"Selected: {path}"); },
                filters: new[] { "*.cs;*.json;*.xml", "*.png;*.jpg", "*.*" },
                filterLabels: new[] { "Code (*.cs, *.json, *.xml)", "Images (*.png, *.jpg)", "All Files (*.*)" })).Show();

            Origami.Button(paper, "btn_save_file", "Save File...", () => EditorApplication.OpenFileDialog(FileDialogMode.Save,
                path => { if (path != null) Toasts.Show("File", $"Save to: {path}", ToastType.Success); })).Show();

            Origami.Button(paper, "btn_select_folder", "Select Folder...", () => EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder,
                path => { if (path != null) Toasts.Show("Folder", $"Selected: {path}"); })).Show();

            Origami.Separator(paper, "sep_filedialog").Show();

            paper.Box("bottom_pad").Height(20);
        });
    }

    private void ResetAll()
    {
        _toggleA = true;
        _toggleB = false;
        _toggleBtnA = false;
        _toggleBtnB = true;
        _textValue = "Hello World";
        _nameValue = "Player One";
        _floatValue = 3.14f;
        _intValue = 42;
        _sliderValue = 0.5f;
        _sliderRange = 75f;
        _dropdownIndex = 0;
        _dropdown2Index = 2;
        _searchText = "";
        _clickCount = 0;
    }
}
