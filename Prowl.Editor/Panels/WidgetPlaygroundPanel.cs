using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.Panels;

[EditorWindow("Debug/Widget Playground")]
public class WidgetPlaygroundPanel : DockPanel
{
    public override string Title => "Widget Playground";

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
    private bool _foldoutOpen = true;
    private bool _foldout2Open;
    private int _dropdownIndex;
    private int _dropdown2Index = 2;
    private string _searchText = "";
    private int _clickCount;
    private Prowl.Vector.Float2 _vec2 = new(1.5f, 2.5f);
    private Prowl.Vector.Float3 _vec3 = new(10f, 20f, 30f);
    private Prowl.Vector.Color _color = new(0.2f, 0.6f, 1f, 1f);
    private float _progress = 0.45f;
    private TestEnum _testEnum = TestEnum.Option2;

    private enum TestEnum { Option1, Option2, Option3, SuperLongOptionName }

    private static readonly string[] Fruits = { "Apple", "Banana", "Cherry", "Date", "Elderberry" };
    private static readonly string[] Modes = { "Constant", "Curve", "Random Between Two" };

    public override void OnGUI(Paper paper, float width, float height)
    {
        using (ScrollView.Begin(paper, "playground_scroll", width, height,
            paddingLeft: 8, paddingRight: 8, paddingTop: 8, rowSpacing: 4))
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
                    textColor: EditorTheme.Text);

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

            // === Toggle Buttons ===
            EditorGUI.Header(paper, "h_togbtn", "Toggle Buttons");

            using (paper.Row("togbtn_row").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                EditorGUI.ToggleButton(paper, "tbtn_a", "Wireframe", _toggleBtnA)
                    .OnValueChanged(v => _toggleBtnA = v);
                EditorGUI.ToggleButton(paper, "tbtn_b", "Grid", _toggleBtnB)
                    .OnValueChanged(v => _toggleBtnB = v);
            }

            EditorGUI.Separator(paper, "sep1b");

            // === Toggles ===
            EditorGUI.Header(paper, "h_tog", "Toggles");

            EditorGUI.Toggle(paper, "tog_a", "Enable Shadows", _toggleA)
                .OnValueChanged(v => _toggleA = v);
            EditorGUI.Toggle(paper, "tog_b", "Cast Reflections", _toggleB)
                .OnValueChanged(v => _toggleB = v);

            EditorGUI.Separator(paper, "sep2");

            // === Text Fields ===
            EditorGUI.Header(paper, "h_txt", "Text Fields");

            EditorGUI.TextField(paper, "tf_text", "Message", _textValue)
                .OnValueChanged(v => _textValue = v);
            EditorGUI.TextField(paper, "tf_name", "Name", _nameValue)
                .OnValueChanged(v => _nameValue = v);

            EditorGUI.Separator(paper, "sep3");

            // === Numeric Fields ===
            EditorGUI.Header(paper, "h_num", "Numeric Fields");

            EditorGUI.FloatField(paper, "ff_float", "Speed", _floatValue)
                .OnValueChanged(v => _floatValue = v);
            EditorGUI.IntField(paper, "if_int", "Health", _intValue)
                .OnValueChanged(v => _intValue = v);

            EditorGUI.Separator(paper, "sep4");

            // === Sliders ===
            EditorGUI.Header(paper, "h_sl", "Sliders");

            EditorGUI.Slider(paper, "sl_norm", "Opacity", _sliderValue, 0f, 1f)
                .OnValueChanged(v => _sliderValue = v);
            EditorGUI.Slider(paper, "sl_range", "Volume", _sliderRange, 0f, 100f)
                .OnValueChanged(v => _sliderRange = v);

            EditorGUI.Separator(paper, "sep5");

            // === Foldouts ===
            EditorGUI.Header(paper, "h_fold", "Foldouts");

            EditorGUI.Foldout(paper, "fo_1", "Advanced Settings", _foldoutOpen)
                .OnValueChanged(v => _foldoutOpen = v);
            if (_foldoutOpen)
            {
                using (paper.Column("fo_1_c").Height(UnitValue.Auto).ChildLeft(16).RowBetween(4).Enter())
                {
                    EditorGUI.FloatField(paper, "fo_speed", "Speed", _floatValue)
                        .OnValueChanged(v => _floatValue = v);
                    EditorGUI.Toggle(paper, "fo_tog", "Enabled", _toggleA)
                        .OnValueChanged(v => _toggleA = v);
                }
            }

            EditorGUI.Foldout(paper, "fo_2", "Debug Options", _foldout2Open)
                .OnValueChanged(v => _foldout2Open = v);
            if (_foldout2Open)
            {
                using (paper.Column("fo_2_c").Height(UnitValue.Auto).ChildLeft(16).RowBetween(4).Enter())
                {
                    EditorGUI.Toggle(paper, "fo_dbg", "Show Wireframe", _toggleB)
                        .OnValueChanged(v => _toggleB = v);
                    EditorGUI.IntField(paper, "fo_iter", "Iterations", _intValue)
                        .OnValueChanged(v => _intValue = v);
                }
            }

            EditorGUI.Separator(paper, "sep6");

            // === Dropdowns ===
            EditorGUI.Header(paper, "h_dd", "Dropdowns");

            EditorGUI.Dropdown(paper, "dd_fruit", "Fruit", _dropdownIndex, Fruits)
                .OnValueChanged(v => _dropdownIndex = v);
            EditorGUI.Dropdown(paper, "dd_mode", "Mode", _dropdown2Index, Modes)
                .OnValueChanged(v => _dropdown2Index = v);

            EditorGUI.Separator(paper, "sep7");

            // === Search Bar ===
            EditorGUI.Header(paper, "h_search", "Search Bar");

            EditorGUI.SearchBar(paper, "sb_1", _searchText, "Type to search...")
                .OnValueChanged(v => _searchText = v);

            EditorGUI.Separator(paper, "sep8");

            // === Enum Dropdown ===
            EditorGUI.Header(paper, "h_enum", "Enum Dropdown");

            EditorGUI.EnumDropdown(paper, "dd_enum", "Test Enum", _testEnum)
                .OnValueChanged(v => _testEnum = v);

            EditorGUI.Separator(paper, "sep8b");

            // === Int Slider ===
            EditorGUI.Header(paper, "h_isl", "Int Slider");

            EditorGUI.IntSlider(paper, "isl_1", "Count", _intSlider, 0, 20)
                .OnValueChanged(v => _intSlider = v);

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
            EditorGUI.Slider(paper, "pb_ctrl", "Progress", _progress, 0f, 1f)
                .OnValueChanged(v => _progress = v);

            EditorGUI.Separator(paper, "sep9");

            // === Context Menu ===
            EditorGUI.Header(paper, "h_ctx", "Context Menu (Right-Click)");

            using (paper.Box("ctx_demo_area")
                .Height(60)
                .BackgroundColor(EditorTheme.InputBackground)
                .Rounded(6)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .ChildLeft(16).ChildTop(8)
                .Enter())
            {
                if (EditorTheme.DefaultFont != null)
                    paper.Box("ctx_hint")
                        .IsNotInteractable()
                        .Text("Right-click here for a context menu", EditorTheme.DefaultFont)
                        .TextColor(EditorTheme.TextDim).FontSize(EditorTheme.FontSize);

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

            paper.Box("bottom_pad").Height(20);
        }
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
        _foldoutOpen = true;
        _foldout2Open = false;
        _dropdownIndex = 0;
        _dropdown2Index = 2;
        _searchText = "";
        _clickCount = 0;
    }
}
