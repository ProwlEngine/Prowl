using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using TextAlign = Prowl.Runtime.UI.TextAlignment;
namespace Prowl.Editor.GUI.Panels;

public class WidgetPlaygroundPanel : DockPanel
{
    [MenuItem("Window/Debug/Widget Playground", priority: 100)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(WidgetPlaygroundPanel));

    public override string Title => "Widget Playground";
    public override string Icon => EditorIcons.Flask;

    private bool _toggleA = true;
    private bool _toggleB;
    private bool _moduleEnabled = true;
    private int _intValue = 42;
    private float _floatValue = 3.14f;
    private string _textValue = "Hello World";
    private TestEnum _testEnum = TestEnum.Option2;
    private TextAlign _textAlign = TextAlign.CenterLeft;

    private Vector.AnimationCurve _curve = new();
    private Vector.Gradient _gradient = new();
    private TestComponent _testObject = new();
    private float _settingsSliderValue = 0.5f;
    private string _settingsColorHex = "#7C5CFF";

    private readonly (string id, string label, string icon)[] _sidebarCats =
    {
        ("general", "General", EditorIcons.Gear),
        ("theme", "Theme", EditorIcons.Palette),
        ("advanced", "Advanced", EditorIcons.Flask),
    };
    private string _sidebarActive = "general";

    private static readonly string[] _swatchPalette =
        ["#7C5CFF", "#4C8CFF", "#2ECC71", "#F5A623", "#E74C3C", "#FF6FB0"];

    private int _dropdownIndex;
    private int _dropdown2Index = 2;
    private string _searchText = "";
    private int _intSlider = 5;
    private Vector.Float2 _vec2 = new(1.5f, 2.5f);
    private Vector.Float3 _vec3 = new(10f, 20f, 30f);
    private Vector.Color _color = new(0.2f, 0.6f, 1f, 1f);
    private float _progress = 0.45f;

    private static readonly string[] Fruits = { "Apple", "Banana", "Cherry", "Date", "Elderberry" };
    private static readonly string[] Modes = { "Constant", "Curve", "Random Between Two" };

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
        public Vector.AnimationCurve SpeedCurve = new();

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

    public enum TestEnum { Option1, Option2, Option3, SuperLongOptionName }

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

    public override void OnGUI(Paper paper, float width, float height)
    {
        Origami.ScrollView(paper, "playground_scroll", width, height)
            .Padding(8, 8, 8, 0)
            .ColSpacing(4)
            .Body(() =>
        {
            // === Row / SettingsRow / SettingsToggle / SettingsSlider / SettingsColorField ===
            EditorGUI.SectionHeader(paper, "h_rows", "Rows", first: true);

            EditorGUI.Row(paper, "row_1", "Plain Row", () =>
                Origami.NumericField<int>(paper, "row_1_v", _intValue, v => _intValue = v).Show());

            EditorGUI.SettingsRow(paper, "sr_1", "Settings Row", () =>
                Origami.NumericField<int>(paper, "sr_1_v", _intValue, v => _intValue = v).Show());

            EditorGUI.SettingsToggle(paper, "st_1", "Settings Toggle", _toggleA, v => _toggleA = v);

            EditorGUI.SettingsSlider(paper, "ss_1", "Settings Slider", _settingsSliderValue,
                0f, 1f, v => _settingsSliderValue = v);

            EditorGUI.SettingsColorField(paper, "scf_1", "Settings Color", () => _settingsColorHex, v => _settingsColorHex = v);

            EditorGUI.Divider(paper, "div_rows");

            // === SliderRow / IntSliderRow (inspector row helpers) ===
            EditorGUI.SectionHeader(paper, "h_inspector_rows", "Inspector Rows");

            EditorGUI.SliderRow(paper, "slr_1", "Slider Row", _floatValue, 0f, 10f, v => _floatValue = v);
            EditorGUI.SliderRow(paper, "slr_2", "Bipolar Slider Row", _floatValue, -5f, 5f, v => _floatValue = v, bipolar: true);
            EditorGUI.IntSliderRow(paper, "islr_1", "Int Slider Row", _intValue, 0, 100, v => _intValue = v);

            EditorGUI.Divider(paper, "div_rows2");

            // === ModuleSection ===
            EditorGUI.SectionHeader(paper, "h_module", "Module Section");
            EditorGUI.ModuleSection(paper, "mod_1", EditorIcons.Gear, "Emission Module", _moduleEnabled, v => _moduleEnabled = v, () =>
            {
                EditorGUI.SliderRow(paper, "mod_1_rate", "Rate", _floatValue, 0f, 10f, v => _floatValue = v);
            });

            EditorGUI.Divider(paper, "div_rows3");

            // === TextAlignmentRow ===
            EditorGUI.SectionHeader(paper, "h_align", "Text Alignment Row");
            EditorGUI.TextAlignmentRow(paper, "align_1", "Alignment", _textAlign, v => _textAlign = v);

            EditorGUI.Divider(paper, "div_rows4");

            // === Project Settings field helpers (Row + widget + auto-save) ===
            EditorGUI.SectionHeader(paper, "h_settings_fields", "Settings Field Helpers");

            EditorGUI.SettingsTextField(paper, "psf_text", "Text Field", _textValue, v => _textValue = v);
            EditorGUI.SettingsIntSlider(paper, "psf_int", "Int Slider", _intValue, 0, 100, v => _intValue = v);
            EditorGUI.SettingsEnumDropdown(paper, "psf_enum", "Enum Dropdown", _testEnum, v => _testEnum = v);
            EditorGUI.SettingsCheckbox(paper, "psf_check", "Checkbox", _toggleA, v => _toggleA = v);
            EditorGUI.SettingsSliderField(paper, "psf_slider", "Slider Field", _floatValue, 0f, 10f, v => _floatValue = v);

            EditorGUI.Divider(paper, "div_rows5");

            // === SectionHeader ===
            EditorGUI.SectionHeader(paper, "h_section", "Section Header (this label)");

            // === Divider ===
            EditorGUI.Divider(paper, "div_1", verticalMargin: 6);

            // === Sidebar ===
            EditorGUI.SectionHeader(paper, "h_sidebar", "Sidebar");
            using (paper.Row("sidebar_demo").Height(120).Enter())
            {
                EditorGUI.Sidebar(paper, "pg_side", _sidebarCats, _sidebarActive, c => _sidebarActive = c, width: 130);
                using (paper.Column("sidebar_content").Margin(12, 0, 0, 0).Enter())
                {
                    if (EditorTheme.DefaultFont != null)
                        paper.Box("sidebar_active_label").IsNotInteractable()
                            .Text($"Active: {_sidebarActive}", EditorTheme.DefaultFont)
                            .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize);
                }
            }

            Origami.Separator(paper, "sep6").Show();

            // === Dropdowns ===
            Origami.Header(paper, "h_dd", "Dropdowns").Show();

            EditorGUI.Row(paper, "dd_fruit", "Fruit", () =>
                Origami.Dropdown(paper, "dd_fruit_v", _dropdownIndex, v => _dropdownIndex = v, Fruits).Show());
            EditorGUI.Row(paper, "dd_mode", "Mode", () =>
                Origami.Dropdown(paper, "dd_mode_v", _dropdown2Index, v => _dropdown2Index = v, Modes).Show());

            Origami.Separator(paper, "sep7").Show();

            // === Search Bar ===
            Origami.Header(paper, "h_search", "Search Bar").Show();

            Origami.SearchField(paper, "sb_1", _searchText, v => _searchText = v, "Type to search...").Show();

            Origami.Separator(paper, "sep8").Show();

            // === Enum Dropdown ===
            Origami.Header(paper, "h_enum", "Enum Dropdown").Show();

            EditorGUI.Row(paper, "dd_enum", "Test Enum", () =>
                Origami.EnumDropdown(paper, "dd_enum_v", _testEnum, v => _testEnum = v).Show());

            Origami.Separator(paper, "sep8b").Show();

            // === Int Slider ===
            Origami.Header(paper, "h_isl", "Int Slider").Show();

            EditorGUI.Row(paper, "isl_1", "Count", () =>
                Origami.IntSlider(paper, "isl_1_v", _intSlider, v => _intSlider = v, 0, 20).Show());

            Origami.Separator(paper, "sep8c").Show();

            // === Vector Fields ===
            Origami.Header(paper, "h_vec", "Vector Fields").Show();

            EditorGUI.Row(paper, "v2_1", "Position 2D", () =>
                Origami.Float2Field(paper, "v2_1_vf", _vec2, v => _vec2 = v).Show());
            EditorGUI.Row(paper, "v3_1", "Position 3D", () =>
                Origami.Float3Field(paper, "v3_1_vf", _vec3, v => _vec3 = v).Show());

            Origami.Separator(paper, "sep8d").Show();

            // === Color Field ===
            Origami.Header(paper, "h_col", "Color Field").Show();

            EditorGUI.Row(paper, "cf_1", "Tint", () =>
                Origami.ColorField(paper, "cf_1_cf", _color, v => _color = v).Show());

            Origami.Separator(paper, "sep8e").Show();

            // === Progress Bar ===
            Origami.Header(paper, "h_prog", "Progress Bar").Show();

            Origami.ProgressBar(paper, "pb_1", _progress).Label("Loading").ShowPercent().Show();
            EditorGUI.Row(paper, "pb_ctrl", "Progress", () =>
                Origami.Slider(paper, "pb_ctrl_v", _progress, v => _progress = v, 0f, 1f)
                    .Format("F2").Show());

            Origami.Separator(paper, "sep9").Show();

            // === Context Menu ===
            Origami.Header(paper, "h_ctx", "Context Menu (Right-Click)").Show();

            using (paper.Box("ctx_demo_area")
                .Height(60)
                .BackgroundColor(EditorTheme.Neutral300)
                .Rounded(Origami.Current.Metrics.ContainerRounding)
                .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .PaddingLeft(16).PaddingTop(8)
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

            EditorGUI.Divider(paper, "div_2", verticalMargin: 6);

            // === Group ===
            EditorGUI.SectionHeader(paper, "h_group", "Group");
            EditorGUI.Group(paper, "grp_1", "Grouped Settings", () =>
            {
                EditorGUI.Row(paper, "grp_1_row", "Nested Row", () =>
                    Origami.Checkbox(paper, "grp_1_cb", _toggleB, v => _toggleB = v).LabelRight("Enabled").Show());
            }, icon: EditorIcons.Gear);

            // === Chip / CtaButton / HeaderIconButton / ToolbarIconBtn ===
            EditorGUI.SectionHeader(paper, "h_buttons", "Chip / CtaButton / Icon Buttons");
            using (paper.Row("chip_row").Height(28).Gap(8).Enter())
            {
                EditorGUI.Chip(paper, "chip_1", "Chip Button", () => { });
                EditorGUI.CtaButton(paper, "cta_1", "Call To Action", EditorTheme.Accent, () => { });
                EditorGUI.HeaderIconButton(paper, "hib_1", EditorIcons.Gear, () => { });
                EditorGUI.ToolbarIconBtn(paper, "tib_1", EditorIcons.Lock, _toggleA, () => _toggleA = !_toggleA);
            }

            EditorGUI.Divider(paper, "div_3", verticalMargin: 6);

            // === EmptyState ===
            EditorGUI.SectionHeader(paper, "h_empty", "Empty State");
            if (EditorTheme.DefaultFont != null)
                EditorGUI.EmptyState(paper, "empty_1", "Nothing to show here.", EditorTheme.DefaultFont);

            EditorGUI.Divider(paper, "div_3b", verticalMargin: 6);

            // === Toasts ===
            EditorGUI.SectionHeader(paper, "h_toast", "Toast Notifications");

            using (paper.Row("toast_row").Height(EditorTheme.RowHeight).Gap(6).Enter())
            {
                Origami.Button(paper, "btn_toast_info", "Info", () => Toasts.Info("Info", "Something happened")).Show();
                Origami.Button(paper, "btn_toast_ok", "Success", () => Toasts.Success("Saved", "Scene saved successfully")).Show();
                Origami.Button(paper, "btn_toast_warn", "Warning", () => Toasts.Warning("Warning", "Asset may be outdated")).Show();
                Origami.Button(paper, "btn_toast_err", "Error", () => Toasts.Error("Error", "Failed to compile shader")).Show();
            }

            EditorGUI.Divider(paper, "div_3c", verticalMargin: 6);

            // === Tooltip ===
            EditorGUI.SectionHeader(paper, "h_tooltip", "Tooltip (hover the button)");

            var tooltipBtn = paper.Box("tooltip_demo")
                .Height(EditorTheme.RowHeight)
                .Width(200)
                .BackgroundColor(EditorTheme.Ink100)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(Origami.Current.Metrics.Rounding)
                .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .Tooltip("This is a tooltip! It appears after a short hover delay.");
            if (EditorTheme.DefaultFont != null)
                tooltipBtn.Text("Hover me for tooltip", EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            EditorGUI.Divider(paper, "div_3d", verticalMargin: 6);

            // === StatChip ===
            EditorGUI.SectionHeader(paper, "h_stat", "Stat Chip");
            if (EditorTheme.DefaultFont != null)
                using (paper.Row("stat_row").Height(22).Gap(6).Enter())
                {
                    EditorGUI.StatChip(paper, "stat_1", "Loaded: 42", EditorTheme.DefaultFont);
                    EditorGUI.StatChip(paper, "stat_2", "Memory: 128 MB", EditorTheme.DefaultFont);
                }

            EditorGUI.Divider(paper, "div_4", verticalMargin: 6);

            // === DropBanner ===
            EditorGUI.SectionHeader(paper, "h_drop", "Drop Banner");
            EditorGUI.DropBanner(paper, "drop_1", "Drop asset here to assign it");

            EditorGUI.Divider(paper, "div_5", verticalMargin: 6);

            // === SwatchRow ===
            EditorGUI.SectionHeader(paper, "h_swatch", "Swatch Row");
            EditorGUI.SwatchRow(paper, EditorSettings.Instance, "swatch_1", "Accent",
                EditorSettings.Instance.Theme.Purple, _swatchPalette);

            EditorGUI.Divider(paper, "div_6", verticalMargin: 6);

            // === Animation Curve ===
            EditorGUI.SectionHeader(paper, "h_curve", "Animation Curve (CurveField)");

            EditorGUI.Row(paper, "curve_1", "Speed Curve", () =>
                CurveField.Create(paper, "curve_1_cf", _curve,
                    v => _curve = v).Show());

            // === Gradient ===
            EditorGUI.SectionHeader(paper, "h_gradient", "Gradient (GradientField)");

            EditorGUI.Row(paper, "gradient_1", "Tint Gradient", () =>
                GradientField.Create(paper, "gradient_1_gf", _gradient,
                    v => _gradient = v).Show());

            EditorGUI.Divider(paper, "div_7", verticalMargin: 6);

            // === Property Grid ===
            EditorGUI.SectionHeader(paper, "h_propgrid", "Property Grid (Reflection)");

            PropertyGridUtils.Draw(paper, "pg_test", _testObject, changed => _testObject = (TestComponent)changed);

            EditorGUI.Divider(paper, "div_8", verticalMargin: 6);

            // === File Dialog (editor-only system integration, not an Origami widget) ===
            EditorGUI.SectionHeader(paper, "h_filedialog", "File Dialog");

            EditorGUI.Chip(paper, "btn_open_file", "Open File...", () => EditorApplication.OpenFileDialog(FileDialogMode.Open,
                path => { if (path != null) Toasts.Show("File", $"Selected: {path}"); },
                filters: new[] { "*.cs;*.json;*.xml", "*.png;*.jpg", "*.*" },
                filterLabels: new[] { "Code (*.cs, *.json, *.xml)", "Images (*.png, *.jpg)", "All Files (*.*)" }));

            paper.Box("bottom_pad").Height(20);
        });
    }
}
