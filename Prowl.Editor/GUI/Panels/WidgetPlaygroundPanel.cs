using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.Editor.Inspector;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;

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

    private Runtime.AnimationCurve _curve = new();
    private Runtime.Gradient _gradient = new();
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
            using (paper.Row("chip_row").Height(28).RowBetween(8).Enter())
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

            // === StatChip ===
            EditorGUI.SectionHeader(paper, "h_stat", "Stat Chip");
            if (EditorTheme.DefaultFont != null)
                using (paper.Row("stat_row").Height(22).RowBetween(6).Enter())
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
