using System.Collections.Generic;

using Prowl.Editor.Docking;
using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.Panels;

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
    private Prowl.Vector.Float2 _vec2 = new(1.5f, 2.5f);
    private Prowl.Vector.Float3 _vec3 = new(10f, 20f, 30f);
    private Prowl.Vector.Color _color = new(0.2f, 0.6f, 1f, 1f);
    private float _progress = 0.45f;
    private TestEnum _testEnum = TestEnum.Option2;

    public enum TestEnum { Option1, Option2, Option3, SuperLongOptionName }
    private Prowl.Runtime.AnimationCurve _curve = new();
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
        public Prowl.Vector.Float2 UV = new(0.5f, 0.75f);
        public Prowl.Vector.Float3 Position = new(1, 2, 3);
        public Prowl.Vector.Float4 Custom = new(0.1f, 0.2f, 0.3f, 0.4f);
        public Prowl.Vector.Color Tint = new(0.5f, 0.8f, 1f, 1f);
        public Prowl.Vector.Quaternion Rotation = Prowl.Vector.Quaternion.Identity;

        // Guid (read-only)
        public System.Guid Id = System.Guid.NewGuid();

        // AnimationCurve
        public Prowl.Runtime.AnimationCurve SpeedCurve = new();

        // Collections
        public List<float> Scores = new() { 10.5f, 20.3f, 30.1f };
        public List<string> Tags = new() { "Player", "Friendly" };
        public int[] LevelData = new int[] { 1, 5, 10, 25, 50 };
        public List<Prowl.Vector.Float3> Waypoints = new()
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
        public Prowl.Vector.Float3 Velocity = new(0, 0, 0);
    }

    public class RenderSettings
    {
        public bool CastShadows = true;
        public bool ReceiveShadows = true;
        public float LODBias = 1.0f;
        public Prowl.Vector.Color EmissionColor = new(0, 0, 0, 1);
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
        public Prowl.Vector.Color FlameColor = new(1f, 0.5f, 0f, 1f);

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
            EditorGUI.Header(paper, "h_col_top", "Color Field");
            EditorGUI.ColorField(paper, "cf_top", "Tint", _color)
                .OnValueChanged(v => _color = v);

            EditorGUI.Separator(paper, "sep_col_top");

            // === Raw Paper TextField test ===
            EditorGUI.Header(paper, "h_raw", "Raw Paper TextField");
            paper.Box("raw_tf_bare")
                .Height(EditorTheme.RowHeight)
                .Width(Prowl.PaperUI.LayoutEngine.UnitValue.Stretch())
                .FontSize(EditorTheme.FontSize)
                .TextField(_textValue, EditorTheme.DefaultFont!,
                    onChange: v => _textValue = v,
                    textColor: EditorTheme.Ink500);

            EditorGUI.Separator(paper, "sep_raw");

            // === Buttons ===
            EditorGUI.Header(paper, "h_btn", "Buttons");

            using (paper.Row("btn_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                EditorGUI.Button(paper, "btn1", "Click Me")
                    .OnValueChanged(v => _clickCount++);
                EditorGUI.Button(paper, "btn2", "Reset")
                    .OnValueChanged(v => ResetAll());
                EditorGUI.Button(paper, "btn3", "Wide Button", width: 160);
            }
            EditorGUI.Label(paper, "btn_count", $"Click count: {_clickCount}");

            EditorGUI.Separator(paper, "sep1");

            // === Toggle switches ===
            EditorGUI.Header(paper, "h_togbtn", "Toggles");

            using (paper.Row("togbtn_row").Height(EditorTheme.RowHeight).RowBetween(12).Enter())
            {
                Origami.Switch(paper, "tbtn_a", _toggleBtnA, v => _toggleBtnA = v)
                    .Primary().LabelRight("Wireframe").Show();
                Origami.Switch(paper, "tbtn_b", _toggleBtnB, v => _toggleBtnB = v)
                    .Primary().LabelRight("Grid").Show();
            }

            EditorGUI.Separator(paper, "sep1b");

            // === Toggles ===
            EditorGUI.Header(paper, "h_tog", "Toggles");

            Origami.Checkbox(paper, "tog_a", _toggleA, v => _toggleA = v).LabelRight("Enable Shadows").Show();
            Origami.Checkbox(paper, "tog_b", _toggleB, v => _toggleB = v).LabelRight("Cast Reflections").Show();

            EditorGUI.Separator(paper, "sep2");

            // === Text Fields ===
            EditorGUI.Header(paper, "h_txt", "Text Fields");

            InspectorRow.Draw(paper, "tf_text", "Message", () =>
                Origami.TextField(paper, "tf_text_v", _textValue, v => _textValue = v).Show());
            InspectorRow.Draw(paper, "tf_name", "Name", () =>
                Origami.TextField(paper, "tf_name_v", _nameValue, v => _nameValue = v).Show());

            EditorGUI.Separator(paper, "sep3");

            // === Numeric Fields ===
            EditorGUI.Header(paper, "h_num", "Numeric Fields");

            InspectorRow.Draw(paper, "ff_float", "Speed", () =>
                Origami.NumericField<float>(paper, "ff_float_v", _floatValue, v => _floatValue = v).Show());
            InspectorRow.Draw(paper, "if_int", "Health", () =>
                Origami.NumericField<int>(paper, "if_int_v", _intValue, v => _intValue = v).Show());

            EditorGUI.Separator(paper, "sep4");

            // === Sliders ===
            EditorGUI.Header(paper, "h_sl", "Sliders");

            InspectorRow.Draw(paper, "sl_norm", "Opacity", () =>
                Origami.Slider(paper, "sl_norm_v", _sliderValue, v => _sliderValue = v, 0f, 1f)
                    .Format("F2").Show());
            InspectorRow.Draw(paper, "sl_range", "Volume", () =>
                Origami.Slider(paper, "sl_range_v", _sliderRange, v => _sliderRange = v, 0f, 100f)
                    .Format("F1").Show());

            EditorGUI.Separator(paper, "sep5");

            // === Foldouts ===
            EditorGUI.Header(paper, "h_fold", "Foldouts");

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

            EditorGUI.Separator(paper, "sep6");

            // === Dropdowns ===
            EditorGUI.Header(paper, "h_dd", "Dropdowns");

            InspectorRow.Draw(paper, "dd_fruit", "Fruit", () =>
                Origami.Dropdown(paper, "dd_fruit_v", _dropdownIndex, v => _dropdownIndex = v, Fruits).Show());
            InspectorRow.Draw(paper, "dd_mode", "Mode", () =>
                Origami.Dropdown(paper, "dd_mode_v", _dropdown2Index, v => _dropdown2Index = v, Modes).Show());

            EditorGUI.Separator(paper, "sep7");

            // === Search Bar ===
            EditorGUI.Header(paper, "h_search", "Search Bar");

            Origami.SearchField(paper, "sb_1", _searchText, v => _searchText = v, "Type to search...").Show();

            EditorGUI.Separator(paper, "sep8");

            // === Enum Dropdown ===
            EditorGUI.Header(paper, "h_enum", "Enum Dropdown");

            InspectorRow.Draw(paper, "dd_enum", "Test Enum", () =>
                Origami.EnumDropdown(paper, "dd_enum_v", _testEnum, v => _testEnum = v).Show());

            EditorGUI.Separator(paper, "sep8b");

            // === Int Slider ===
            EditorGUI.Header(paper, "h_isl", "Int Slider");

            InspectorRow.Draw(paper, "isl_1", "Count", () =>
                Origami.IntSlider(paper, "isl_1_v", _intSlider, v => _intSlider = v, 0, 20).Show());

            EditorGUI.Separator(paper, "sep8c");

            // === Vector Fields ===
            EditorGUI.Header(paper, "h_vec", "Vector Fields");

            EditorGUI.Vector2Field(paper, "v2_1", "Position 2D", _vec2)
                .OnValueChanged(v => _vec2 = v);
            EditorGUI.Vector3Field(paper, "v3_1", "Position 3D", _vec3)
                .OnValueChanged(v => _vec3 = v);

            EditorGUI.Separator(paper, "sep8d");

            // === Color Field ===
            EditorGUI.Header(paper, "h_col", "Color Field");

            EditorGUI.ColorField(paper, "cf_1", "Tint", _color);

            EditorGUI.Separator(paper, "sep8e");

            // === Progress Bar ===
            EditorGUI.Header(paper, "h_prog", "Progress Bar");

            EditorGUI.ProgressBar(paper, "pb_1", "Loading", _progress);
            InspectorRow.Draw(paper, "pb_ctrl", "Progress", () =>
                Origami.Slider(paper, "pb_ctrl_v", _progress, v => _progress = v, 0f, 1f)
                    .Format("F2").Show());

            EditorGUI.Separator(paper, "sep9");

            // === Context Menu ===
            EditorGUI.Header(paper, "h_ctx", "Context Menu (Right-Click)");

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

                ContextMenuHelper.RightClickMenu(paper, "ctx_test", menu =>
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

            EditorGUI.Separator(paper, "sep10");

            // === Modal Dialog ===
            EditorGUI.Header(paper, "h_modal", "Modal Dialog");

            using (paper.Row("modal_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                EditorGUI.Button(paper, "btn_confirm", "Confirm Dialog")
                    .OnValueChanged(v => ModalDialog.Confirm("Delete Object",
                        "Are you sure you want to delete this object?",
                        () => Toasts.Success("Deleted", "Object was deleted"),
                        () => Toasts.Info("Cancelled", "Deletion cancelled")));

                EditorGUI.Button(paper, "btn_message", "Message Dialog")
                    .OnValueChanged(v => ModalDialog.Message("Info", "This is a message dialog."));
            }

            EditorGUI.Separator(paper, "sep11");

            // === Toasts ===
            EditorGUI.Header(paper, "h_toast", "Toast Notifications");

            using (paper.Row("toast_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                EditorGUI.Button(paper, "btn_toast_info", "Info")
                    .OnValueChanged(v => Toasts.Info("Info", "Something happened"));
                EditorGUI.Button(paper, "btn_toast_ok", "Success")
                    .OnValueChanged(v => Toasts.Success("Saved", "Scene saved successfully"));
                EditorGUI.Button(paper, "btn_toast_warn", "Warning")
                    .OnValueChanged(v => Toasts.Warning("Warning", "Asset may be outdated"));
                EditorGUI.Button(paper, "btn_toast_err", "Error")
                    .OnValueChanged(v => Toasts.Error("Error", "Failed to compile shader"));
            }

            EditorGUI.Separator(paper, "sep12");

            // === Tooltip ===
            EditorGUI.Header(paper, "h_tooltip", "Tooltip (hover the button)");

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

            EditorGUI.Separator(paper, "sep13");

            // === Animation Curve ===
            EditorGUI.Header(paper, "h_curve", "Animation Curve");

            CurveEditor.CurveField(paper, "curve_1", "Speed Curve", _curve)
                .OnValueChanged(v => _curve = v);

            EditorGUI.Separator(paper, "sep14");

            // === Property Grid ===
            EditorGUI.Header(paper, "h_propgrid", "Property Grid (Reflection)");

            PropertyGrid.Draw(paper, "pg_test", _testObject, changed => _testObject = (TestComponent)changed);

            EditorGUI.Separator(paper, "sep15");

            // === Live State ===
            EditorGUI.Header(paper, "h_state", "Current State");
            EditorGUI.Label(paper, "st_click", $"Clicks: {_clickCount}");
            EditorGUI.Label(paper, "st_togbtn", $"Wireframe: {_toggleBtnA}  Grid: {_toggleBtnB}");
            EditorGUI.Label(paper, "st_tog", $"Shadows: {_toggleA}  Reflections: {_toggleB}");
            EditorGUI.Label(paper, "st_text", $"Message: {_textValue}");
            EditorGUI.Label(paper, "st_name", $"Name: {_nameValue}");
            EditorGUI.Label(paper, "st_float", $"Speed: {_floatValue:F2}");
            EditorGUI.Label(paper, "st_int", $"Health: {_intValue}");
            EditorGUI.Label(paper, "st_sl1", $"Opacity: {_sliderValue:F2}");
            EditorGUI.Label(paper, "st_sl2", $"Volume: {_sliderRange:F1}");
            EditorGUI.Label(paper, "st_dd1", $"Fruit: {Fruits[_dropdownIndex]}");
            EditorGUI.Label(paper, "st_dd2", $"Mode: {Modes[_dropdown2Index]}");
            EditorGUI.Label(paper, "st_search", $"Search: \"{_searchText}\"");
            EditorGUI.Label(paper, "st_enum", $"Enum: {_testEnum}");
            EditorGUI.Label(paper, "st_isl", $"IntSlider: {_intSlider}");
            EditorGUI.Label(paper, "st_vec2", $"Vec2: ({_vec2.X:F1}, {_vec2.Y:F1})");
            EditorGUI.Label(paper, "st_vec3", $"Vec3: ({_vec3.X:F1}, {_vec3.Y:F1}, {_vec3.Z:F1})");
            EditorGUI.Label(paper, "st_col", $"Color: R={_color.R:F2} G={_color.G:F2} B={_color.B:F2}");
            EditorGUI.Label(paper, "st_prog", $"Progress: {_progress:P0}");

            // ── File Dialog ──
            EditorGUI.Header(paper, "h_filedialog", "File Dialog");

            EditorGUI.Button(paper, "btn_open_file", "Open File...")
                .OnValueChanged(_ => Widgets.FileDialog.Open(Widgets.FileDialogMode.Open,
                    path => { if (path != null) Widgets.Toasts.Show("File", $"Selected: {path}"); },
                    filters: new[] { "*.cs;*.json;*.xml", "*.png;*.jpg", "*.*" },
                    filterLabels: new[] { "Code (*.cs, *.json, *.xml)", "Images (*.png, *.jpg)", "All Files (*.*)" }));

            EditorGUI.Button(paper, "btn_save_file", "Save File...")
                .OnValueChanged(_ => Widgets.FileDialog.Open(Widgets.FileDialogMode.Save,
                    path => { if (path != null) Widgets.Toasts.Show("File", $"Save to: {path}", Widgets.ToastType.Success); }));

            EditorGUI.Button(paper, "btn_select_folder", "Select Folder...")
                .OnValueChanged(_ => Widgets.FileDialog.Open(Widgets.FileDialogMode.SelectFolder,
                    path => { if (path != null) Widgets.Toasts.Show("Folder", $"Selected: {path}"); }));

            EditorGUI.Separator(paper, "sep_filedialog");

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
