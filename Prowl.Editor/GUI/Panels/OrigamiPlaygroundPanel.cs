// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.Panels;

// -- PropertyGrid demo types --------------------------------

public enum DemoWeaponType { Sword, Bow, Staff, Dagger, Hammer }

[Flags]
public enum DemoStatusFlags
{
    None = 0,
    Poisoned = 1 << 0,
    Burning = 1 << 1,
    Frozen = 1 << 2,
    Stunned = 1 << 3,
    Blessed = 1 << 4,
}

public class DemoNestedStats
{
    public int Strength = 10;
    public int Agility = 8;
    public int Intelligence = 12;
    [Range(1, 100)] public float CritChance = 15f;
}

public class DemoPropertyGridTarget
{
    // -- Basics --
    [Header("Identity")]
    public string Name = "Hero";
    public bool IsActive = true;
    public int Level = 5;
    [Range(0, 100)] public float Health = 75f;
    [Range(0, 1)] public float Armor = 0.3f;

    [Space]
    [Header("Appearance")]
    public Vector.Color TintColor = new(0.2f, 0.6f, 1f, 1f);
    public Vector.Float3 Position = new(1f, 2f, 3f);
    public Vector.Float2 UVOffset = new(0f, 0f);

    [Space]
    [Header("Combat")]
    public DemoWeaponType Weapon = DemoWeaponType.Sword;
    public DemoStatusFlags Status = DemoStatusFlags.Poisoned | DemoStatusFlags.Blessed;
    [Tooltip("Damage dealt per hit before modifiers")]
    public float BaseDamage = 25f;

    [Space]
    [Header("Description")]
    [TextArea(3, 6)]
    public string Bio = "A brave adventurer.\nReady for anything.";

    [Space]
    [Header("Inventory")]
    public List<string> Inventory = new() { "Health Potion", "Iron Sword", "Torch" };
    public List<int> LootTable = new() { 10, 25, 50, 100 };

    [Space]
    [Header("Stats")]
    public DemoNestedStats Stats = new();

    [Space]
    [Header("Conditional Fields")]
    public bool HasMagic = false;
    [ShowIf("HasMagic")] public float ManaPool = 100f;
    [ShowIf("HasMagic")] [Range(0, 10)] public int SpellSlots = 3;

    [Space]
    [Header("Read-Only Data")]
    [ReadOnly] public string UniqueId = "hero_001";
    [ReadOnly] public int CreatedFrame = 42;

    [Space]
    public double PreciseCoord = 3.14159265358979;
    public long BigNumber = 9999999999L;
    public byte SmallValue = 128;

    [Button("Reset Health")]
    public void ResetHealth() => Health = 100f;

    [Button("Randomize Color")]
    public void RandomizeColor()
    {
        var rng = new Random();
        TintColor = new Vector.Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), 1f);
    }
}

/// <summary>
/// Stress-test surface for every Origami widget. Currently focused on
/// <see cref="DropdownBuilder{T}"/> and <see cref="MultiDropdownBuilder{T}"/> - every
/// feature exposed on those builders has at least one concrete demo so visual regressions
/// are easy to spot.
/// </summary>
[EditorWindow("Debug/Origami Playground")]
public class OrigamiPlaygroundPanel : DockPanel
{
    public override string Title => "Origami Playground";
    public override string Icon => EditorIcons.Flask;

    // -- Demo enums ----------------------------------------------

    public enum Color8 { Red, Orange, Yellow, Green, Blue, Indigo, Violet, Black }

    [Flags]
    public enum Compass
    {
        None = 0, North = 1 << 0, South = 1 << 1, East = 1 << 2, West = 1 << 3,
        Up = 1 << 4, Down = 1 << 5, Forward = 1 << 6, Back = 1 << 7,
    }

    private record Country(string Code, string Name, string Continent);

    // -- State --------------------------------------------------

    private string _basicFruit = "Banana";
    private int _intIndex;
    private Color8 _enum = Color8.Green;

    private string _search = "";
    private string _searchCustom = "";
    private string _searchBig = "";

    private int _paged;
    private int _pagedSearchable;

    private string _withIcon = "Star";
    private string _withSecondary = "Dec 2024";
    private string _withDisabled = "Free";
    private string _customRender = "Banana";
    private string _customTrigger = "Banana";

    private string _vDefault = "Banana";
    private string _vPrimary = "Banana";
    private string _vSuccess = "Banana";
    private string _vWarning = "Banana";
    private string _vDanger  = "Banana";
    private string _vInfo    = "Banana";
    private string _vSubtle  = "Banana";

    private string _szNarrow = "Apple";
    private string _szWide = "Apple";
    private string _szShort = "Apple";
    private string _szWidePopover = "Apple";

    private List<string> _multiBasic = new() { "Banana" };
    private List<string> _multiSearch = new() { "Apple", "Cherry" };
    private List<string> _multiSummary = new() { "Apple", "Banana", "Cherry", "Date" };

    private Compass _flags = Compass.North | Compass.East;
    private Compass _flagsAll;

    private Country _country = new("US", "United States", "North America");
    private Country _countryEqualityByCode = new("US", "United States v2", "North America");

    // -- VectorField state -------------------------------------
    private Vector.Float2 _vfFloat2 = new(1.5f, 2.5f);
    private Vector.Float3 _vfFloat3 = new(1f, 2f, 3f);
    private Vector.Float4 _vfFloat4 = new(1f, 2f, 3f, 4f);
    private Vector.Int2 _vfInt2 = new(10, 20);
    private Vector.Double3 _vfDouble3 = new(1.1, 2.2, 3.3);

    // -- ColorField state --------------------------------------
    private Vector.Color _cfBasic = new(0.3f, 0.6f, 0.9f, 1f);
    private Vector.Color _cfNoAlpha = new(0.9f, 0.2f, 0.3f, 1f);
    private Vector.Color _cfReadOnly = new(0.2f, 0.8f, 0.4f, 1f);
    private Vector.Color _cfNoPalette = new(0.7f, 0.5f, 0.1f, 1f);

    // -- TextField state ----------------------------------------
    private string _txtPlain = "";
    private string _txtSearch = "";
    private string _txtPassword = "hunter2";
    private string _txtMulti = "Type a few lines.\nWraps automatically when DoWrap is on.";
    private string _txtAutoComplete = "";
    private string _txtClearBtn = "Clear me with the X >";
    private string _txtReadOnly = "Read-only - content can't change.";
    private string _txtIntFilter = "";
    private string _txtFloatFilter = "";
    private string _txtAlphaNum = "";
    private string _txtNoSpaces = "";
    private string _txtError = "bad";
    private string _txtValidator = "abc";
    private string _txtHelper = "";
    private string _txtLeading = "";
    private string _txtTrailing = "";

    // -- Toggle state ------------------------------------------
    public enum ShipMode { Standard, Express, Overnight }
    public enum Theme { System, Light, Dark }

    private bool _swBasic = true;
    private bool _swDanger = false;
    private bool _swDisabled = true;
    private bool _swReadOnly = true;
    private bool _swLabelLeft = true;
    private bool _swSettings1 = true;
    private bool _swSettings3 = true;
    private bool _swOnOffText = true;
    private bool _swYesNo = false;
    private bool _swGlyph = true;
    private bool _swSmall = true;
    private bool _swMedium = false;
    private bool _swLarge = true;
    private bool _swCustom = false;

    private bool _cbBasic = true;
    private bool _cbDanger = false;
    private bool _cbA = true, _cbB, _cbC = true;
    private bool _cbError;
    private bool _cbHelper;

    private bool _rdSingle = false;
    private ShipMode _rgShip = ShipMode.Express;
    private ShipMode _rgShipH = ShipMode.Standard;
    private Theme _rgTheme = Theme.System;

    // -- Slider state ------------------------------------------
    private float _slBasic = 0.5f;
    private int _slIntBasic = 50;
    private float _slStep = 0.5f;
    private float _slLog = 100f;
    private float _slBipolar = 0f;
    private float _slContrast = 0f;
    private float _slDisabled = 0.7f;
    private float _slReadOnly = 0.3f;
    private float _slErr = 0.4f;
    private float _slCustom = 0.5f;
    private float _slTicks = 25f;
    private float _slTickLabels = 50f;
    private float _slNoValue = 0.5f;
    private float _slWideFmt = 1234.567f;
    private float _slPrimary = 0.5f;
    private float _slSuccess = 0.5f;
    private float _slWarning = 0.5f;
    private float _slDanger = 0.5f;
    private float _slInfo = 0.5f;
    private float _slSubtle = 0.5f;
    private float _slSmall = 0.5f;
    private float _slMedium = 0.5f;
    private float _slLarge = 0.5f;
    private float _slVert = 0.5f;
    private int _slDragCount;
    private int _slDragEndCount;

    // -- RangeSlider state -------------------------------------
    private float _rsLow = 0.25f, _rsHigh = 0.75f;
    private int _rsIntLow = 20, _rsIntHigh = 80;
    private float _rsStepLow = 10f, _rsStepHigh = 60f;
    private float _rsMinDistLow = 30f, _rsMinDistHigh = 70f;
    private float _rsNoSwapLow = 30f, _rsNoSwapHigh = 70f;
    private float _rsTickLow = 25f, _rsTickHigh = 75f;

    // -- Button state ------------------------------------------
    private int _btnClickCount;
    private int _btnRightClickCount;
    private int _btnDoubleClickCount;
    private bool _btnLoading;
    private bool _btnPulse;
    private bool _btnShadow = true;
    private bool _btnDisabled;
    private int _bgViewMode;        // ButtonGroup: 0/1/2
    private int _bgAlign;           // ButtonGroup w/ icons
    private int _bgSize = 1;        // ButtonGroup sizes
    private int _bgVariant = 1;     // ButtonGroup variants demo

    // -- NumericField state ------------------------------------
    private float _numFloat = 1.5f;
    private double _numDouble = 3.14159265;
    private decimal _numDecimal = 9.99m;
    private int _numInt = 42;
    private uint _numUint = 100u;
    private long _numLong = 1234567890L;
    private short _numShort = 12;
    private byte _numByte = 200;
    private sbyte _numSbyte = -10;
    private float _numClamped = 50f;
    private float _numStep = 5f;
    private float _numFormatted = 1234.567f;
    private float _numInvariant = 1.5f;

    // -- Static datasets ----------------------------------------

    private static readonly string[] s_fruits =
    {
        "Apple","Banana","Cherry","Date","Elderberry","Fig","Grape","Honeydew",
    };

    private static readonly string[] s_greek =
    {
        "Alpha","Beta","Gamma","Delta","Epsilon","Zeta","Eta","Theta","Iota","Kappa",
        "Lambda","Mu","Nu","Xi","Omicron","Pi","Rho","Sigma","Tau","Upsilon",
        "Phi","Chi","Psi","Omega",
    };

    private static readonly string[] s_bigList = Enumerable.Range(1, 200).Select(i => $"Entry {i:000}").ToArray();

    private static readonly (string Glyph, string Name)[] s_iconRows =
    {
        ("★", "Star"),       // ★
        ("♥", "Heart"),      // ♥
        ("♠", "Spade"),      // ♠
        ("♦", "Diamond"),    // ♦
        ("♣", "Club"),       // ♣
        ("☀", "Sun"),        // ☀
        ("☃", "Snowman"),    // ☃
    };

    private static readonly (string Title, string Date)[] s_secondaryRows =
    {
        ("Dec 2024", "v1.0"),
        ("Jan 2025", "v1.1"),
        ("Feb 2025", "v1.2"),
        ("Mar 2025", "v2.0"),
        ("Apr 2025", "v2.1"),
    };

    private static readonly string[] s_planTiers = { "Free", "Pro", "Team", "Enterprise" };

    private static readonly Country[] s_countries =
    {
        new("US", "United States",  "North America"),
        new("CA", "Canada",         "North America"),
        new("MX", "Mexico",         "North America"),
        new("BR", "Brazil",         "South America"),
        new("AR", "Argentina",      "South America"),
        new("UK", "United Kingdom", "Europe"),
        new("FR", "France",         "Europe"),
        new("DE", "Germany",        "Europe"),
        new("ES", "Spain",          "Europe"),
        new("IT", "Italy",          "Europe"),
        new("JP", "Japan",          "Asia"),
        new("KR", "South Korea",    "Asia"),
        new("CN", "China",          "Asia"),
        new("IN", "India",          "Asia"),
        new("AU", "Australia",      "Oceania"),
        new("NZ", "New Zealand",    "Oceania"),
        new("ZA", "South Africa",   "Africa"),
        new("NG", "Nigeria",        "Africa"),
        new("EG", "Egypt",          "Africa"),
    };

    // -- Tree state ---------------------------------------------
    private string? _treeSelectedId;
    private HashSet<string> _treeChecked = new() { "src", "main_cs" };

    // -- PropertyGrid state ------------------------------------
    private DemoPropertyGridTarget _pgTarget = new();
    private DemoPropertyGridTarget _pgTarget2 = new()
    {
        Name = "Goblin",
        Level = 2,
        Health = 30f,
        Weapon = DemoWeaponType.Dagger,
        TintColor = new Vector.Color(0.4f, 0.8f, 0.2f, 1f),
        HasMagic = true,
        ManaPool = 50f,
    };

    // -- Label state --------------------------------------------
    private int _labelClickCount;

    // -- Loading state ------------------------------------------
    private float _loadProgress = 0.42f;

    // -- OnGUI --------------------------------------------------

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Origami.ScrollView(paper, "op_scroll", width, height)
            .Padding(12, 12, 12, 12)
            .ColSpacing(8)
            .Body(() =>
            {
                Origami.Header(paper, "op_h_root", "Origami Dropdown Playground").Show();

                paper.Box("op_intro").Height(20)
                    .Alignment(TextAlignment.MiddleLeft).IsNotInteractable()
                    .Text("Every demo controls a real value. Open multiple - they're all independent.", font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 1);

                Section_Basics(paper);
                Section_Search(paper);
                Section_Pagination(paper);
                Section_Rendering(paper);
                Section_Variants(paper);
                Section_Sizing(paper);
                Section_MultiSelect(paper);
                Section_Flags(paper);
                Section_TextFieldBasics(paper);
                Section_TextFieldFilters(paper);
                Section_TextFieldValidation(paper);
                Section_TextFieldAutoComplete(paper);
                Section_NumericTypes(paper);
                Section_NumericClampStep(paper);
                Section_NumericCulture(paper);
                Section_ToggleBasics(paper);
                Section_ToggleVariants(paper);
                Section_ToggleSizes(paper);
                Section_ToggleStates(paper);
                Section_SwitchExtras(paper);
                Section_CheckboxExtras(paper);
                Section_RadioGroups(paper);
                Section_SliderBasics(paper);
                Section_SliderVariants(paper);
                Section_SliderSizes(paper);
                Section_SliderScale(paper);
                Section_SliderTicks(paper);
                Section_SliderStates(paper);
                Section_SliderExtras(paper);
                Section_SliderVertical(paper);
                Section_RangeSliderShowcase(paper);
                Section_Buttons(paper);
                Section_VectorFields(paper);
                Section_ColorFields(paper);
                Section_Headers(paper);
                Section_AppBar(paper);
                Section_ContextMenus(paper);
                Section_Modals(paper);
                Section_PropertyGrid(paper);
                Section_Labels(paper);
                Section_Loading(paper);
                Section_Tree(paper);
                Section_State(paper);
            });
    }

    // -- Sections -----------------------------------------------

    private void Section_Basics(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_basics", "Basics").DefaultExpanded().Body(() =>
        {
            using (paper.Column("op_basics_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "single_string", "Single (string list)", () =>
                    Origami.Dropdown(paper, "op_b_single", _basicFruit, v => _basicFruit = v, s_fruits).Show());

                LabelRow(paper, "single_index", "Single (string-array index overload)", () =>
                    Origami.Dropdown(paper, "op_b_index", _intIndex, v => _intIndex = v,
                        new[] { "First", "Second", "Third" }).Show());

                LabelRow(paper, "enum", "EnumDropdown", () =>
                    Origami.EnumDropdown(paper, "op_b_enum", _enum, v => _enum = v).Show());

                LabelRow(paper, "country", "Typed item with Display + Secondary", () =>
                    Origami.Dropdown(paper, "op_b_country", _country, v => _country = v, s_countries)
                        .Display(c => c.Name)
                        .Secondary(c => c.Continent)
                        .Show());

                LabelRow(paper, "country_eq", "Custom Comparer (equal by Code, not record value)", () =>
                    Origami.Dropdown(paper, "op_b_country_eq", _countryEqualityByCode,
                            v => _countryEqualityByCode = v, s_countries)
                        .Display(c => $"{c.Code} > {c.Name}")
                        .Comparer(EqualityComparer<Country>.Create((a, b) =>
                            (a == null && b == null) || (a != null && b != null && a.Code == b.Code),
                            c => c?.Code.GetHashCode() ?? 0))
                        .Show());
            }
        });
    }

    private void Section_Search(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_search", "Search").Body(() =>
        {
            using (paper.Column("op_search_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "search_basic", "Searchable (default substring filter)", () =>
                    Origami.Dropdown(paper, "op_s_basic", _search, v => _search = v, s_greek)
                        .Searchable("Type a Greek letter...")
                        .Show());

                LabelRow(paper, "search_custom", "Searchable (custom filter > prefix only)", () =>
                    Origami.Dropdown(paper, "op_s_custom", _searchCustom, v => _searchCustom = v, s_greek)
                        .Searchable("Prefix match...")
                        .SearchFilter((item, q) => item.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                        .Show());

                LabelRow(paper, "search_big", "Searchable + 200 entries + MaxHeight 240", () =>
                    Origami.Dropdown(paper, "op_s_big", _searchBig, v => _searchBig = v, s_bigList)
                        .Searchable()
                        .MaxHeight(240)
                        .Show());
            }
        });
    }

    private void Section_Pagination(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_pages", "Pagination").Body(() =>
        {
            using (paper.Column("op_pages_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "paged_basic", "PageSize 5", () =>
                    Origami.Dropdown(paper, "op_p_basic", _paged, v => _paged = v,
                            Enumerable.Range(1, 30).ToArray())
                        .Display(i => $"Item #{i}")
                        .PageSize(5)
                        .Show());

                LabelRow(paper, "paged_search", "PageSize 10 + Searchable + MaxHeight 200", () =>
                    Origami.Dropdown(paper, "op_p_search", _pagedSearchable, v => _pagedSearchable = v,
                            Enumerable.Range(1, 200).ToArray())
                        .Display(i => $"Entry {i:000}")
                        .PageSize(10)
                        .MaxHeight(200)
                        .Searchable("Search 200 entries...")
                        .Show());
            }
        });
    }

    private void Section_Rendering(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_render", "Custom rendering").Body(() =>
        {
            using (paper.Column("op_render_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "icon", "Icon per row", () =>
                    Origami.Dropdown(paper, "op_r_icon", _withIcon, v => _withIcon = v,
                            s_iconRows.Select(r => r.Name).ToArray())
                        .Icon(name =>
                        {
                            for (int i = 0; i < s_iconRows.Length; i++)
                                if (s_iconRows[i].Name == name) return s_iconRows[i].Glyph;
                            return string.Empty;
                        })
                        .Show());

                LabelRow(paper, "secondary", "Secondary trailing text", () =>
                    Origami.Dropdown(paper, "op_r_sec", _withSecondary, v => _withSecondary = v,
                            s_secondaryRows.Select(r => r.Title).ToArray())
                        .Secondary(name =>
                        {
                            for (int i = 0; i < s_secondaryRows.Length; i++)
                                if (s_secondaryRows[i].Title == name) return s_secondaryRows[i].Date;
                            return string.Empty;
                        })
                        .Show());

                LabelRow(paper, "disabled", "IsItemEnabled (Pro+ disabled)", () =>
                    Origami.Dropdown(paper, "op_r_dis", _withDisabled, v => _withDisabled = v, s_planTiers)
                        .IsItemEnabled(p => p == "Free" || p == "Pro")
                        .Show());

                LabelRow(paper, "item_render", "Custom row (colored swatches)", () =>
                    Origami.Dropdown(paper, "op_r_custom", _customRender, v => _customRender = v, s_fruits)
                        .ItemRender((fruit, ctx) =>
                        {
                            paper.Box($"op_r_custom_sw_{fruit}")
                                .Width(14).Height(14).Rounded(7)
                                .Margin(0, 6, (ctx.RowHeight - 14) / 2f, 0)
                                .BackgroundColor(FruitColor(fruit))
                                .IsNotInteractable();
                            paper.Box($"op_r_custom_lbl_{fruit}")
                                .Width(UnitValue.Stretch())
                                .Alignment(TextAlignment.MiddleLeft)
                                .Text(fruit, EditorTheme.DefaultFont)
                                .TextColor(ctx.IsSelected ? ctx.Ink.C600 : ctx.Ink.C500)
                                .FontSize(EditorTheme.FontSize)
                                .IsNotInteractable();
                        })
                        .Show());

                LabelRow(paper, "custom_trigger", "CustomTrigger (chip style)", () =>
                    Origami.Dropdown(paper, "op_r_cttrig", _customTrigger, v => _customTrigger = v, s_fruits)
                        .CustomTrigger(ctx =>
                        {
                            paper.Box("op_r_cttrig_chip")
                                .Width(10).Height(10).Rounded(5)
                                .Margin(0, 6, 7, 0)
                                .BackgroundColor(FruitColor(ctx.DisplayText))
                                .IsNotInteractable();
                            paper.Box("op_r_cttrig_lbl")
                                .Width(UnitValue.Stretch())
                                .Alignment(TextAlignment.MiddleLeft)
                                .Text($"Currently: {ctx.DisplayText}", EditorTheme.DefaultFont)
                                .TextColor(ctx.Ink.C500).FontSize(EditorTheme.FontSize)
                                .IsNotInteractable();
                            paper.Box("op_r_cttrig_chev")
                                .Width(14)
                                .Alignment(TextAlignment.MiddleCenter)
                                .Text(ctx.IsOpen ? "^" : "v", EditorTheme.DefaultFont)
                                .TextColor(ctx.Ink.C400).FontSize(EditorTheme.FontSize * 0.85f)
                                .IsNotInteractable();
                        })
                        .Show());
            }
        });
    }

    private void Section_Variants(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_variants", "Variants").Body(() =>
        {
            using (paper.Column("op_var_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "v_default", "Default", () =>
                    Origami.Dropdown(paper, "op_v_def", _vDefault, v => _vDefault = v, s_fruits).Show());
                LabelRow(paper, "v_primary", "Primary", () =>
                    Origami.Dropdown(paper, "op_v_pri", _vPrimary, v => _vPrimary = v, s_fruits).Primary().Show());
                LabelRow(paper, "v_success", "Success", () =>
                    Origami.Dropdown(paper, "op_v_suc", _vSuccess, v => _vSuccess = v, s_fruits).Success().Show());
                LabelRow(paper, "v_warning", "Warning", () =>
                    Origami.Dropdown(paper, "op_v_war", _vWarning, v => _vWarning = v, s_fruits).Warning().Show());
                LabelRow(paper, "v_danger", "Danger", () =>
                    Origami.Dropdown(paper, "op_v_dan", _vDanger, v => _vDanger = v, s_fruits).Danger().Show());
                LabelRow(paper, "v_info", "Info", () =>
                    Origami.Dropdown(paper, "op_v_inf", _vInfo, v => _vInfo = v, s_fruits).Info().Show());
                LabelRow(paper, "v_subtle", "Subtle", () =>
                    Origami.Dropdown(paper, "op_v_sub", _vSubtle, v => _vSubtle = v, s_fruits).Subtle().Show());
            }
        });
    }

    private void Section_Sizing(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sizing", "Sizing").Body(() =>
        {
            using (paper.Column("op_sz_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "narrow", "Width 120 (narrow trigger)", () =>
                    Origami.Dropdown(paper, "op_sz_n", _szNarrow, v => _szNarrow = v, s_fruits)
                        .Width(120)
                        .Show());

                LabelRow(paper, "wide_pop", "PopoverWidth 360 over narrow trigger", () =>
                    Origami.Dropdown(paper, "op_sz_wp", _szWidePopover, v => _szWidePopover = v, s_fruits)
                        .Width(160)
                        .PopoverWidth(360)
                        .Show());

                LabelRow(paper, "tall_item", "ItemHeight 36 + EmptyText override", () =>
                    Origami.Dropdown(paper, "op_sz_t", _szWide, v => _szWide = v, s_fruits)
                        .ItemHeight(36)
                        .Searchable()
                        .EmptyText("No matching fruit found.")
                        .Show());

                LabelRow(paper, "short_max", "MaxHeight 100 (force scroll)", () =>
                    Origami.Dropdown(paper, "op_sz_s", _szShort, v => _szShort = v, s_greek)
                        .MaxHeight(100)
                        .Show());
            }
        });
    }

    private void Section_MultiSelect(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_multi", "Multi-select").Body(() =>
        {
            using (paper.Column("op_m_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "multi_basic", "Basic", () =>
                    Origami.MultiDropdown<string>(paper, "op_m_basic", _multiBasic,
                            list => _multiBasic = list.ToList(), s_fruits)
                        .Show());

                LabelRow(paper, "multi_search", "Searchable", () =>
                    Origami.MultiDropdown<string>(paper, "op_m_search", _multiSearch,
                            list => _multiSearch = list.ToList(), s_greek)
                        .Searchable()
                        .Show());

                LabelRow(paper, "multi_summary", "SummaryItemLimit 1 + custom format", () =>
                    Origami.MultiDropdown<string>(paper, "op_m_sum", _multiSummary,
                            list => _multiSummary = list.ToList(), s_fruits)
                        .SummaryItemLimit(1)
                        .SummaryFormat("{0} fruits")
                        .Show());
            }
        });
    }

    private void Section_Flags(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_flags", "Flags").Body(() =>
        {
            using (paper.Column("op_f_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "flags_basic", "FlagsDropdown", () =>
                    Origami.FlagsDropdown(paper, "op_f_b", _flags, v => _flags = v).Show());

                LabelRow(paper, "flags_summary", "FlagsDropdown + custom summary", () =>
                    Origami.FlagsDropdown(paper, "op_f_s", _flagsAll, v => _flagsAll = v)
                        .SummaryItemLimit(0)
                        .SummaryFormat("{0} directions")
                        .Show());
            }
        });
    }

    // -- TextField sections -------------------------------------

    private void Section_TextFieldBasics(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tf_basics", "TextField - basics").Body(() =>
        {
            using (paper.Column("op_tf_basics_col").Height(UnitValue.Auto).RowBetween(8).Enter())
            {
                LabelRow(paper, "tf_plain", "Plain TextField", () =>
                    Origami.TextField(paper, "op_tf_plain", _txtPlain, v => _txtPlain = v)
                        .Placeholder("Type something...")
                        .Show());

                LabelRow(paper, "tf_search", "SearchField (sugar)", () =>
                    Origami.SearchField(paper, "op_tf_search", _txtSearch, v => _txtSearch = v).Show());

                LabelRow(paper, "tf_password", "PasswordField (eye toggle)", () =>
                    Origami.PasswordField(paper, "op_tf_pw", _txtPassword, v => _txtPassword = v).Show());

                LabelRow(paper, "tf_clear", "ClearButton", () =>
                    Origami.TextField(paper, "op_tf_clear", _txtClearBtn, v => _txtClearBtn = v)
                        .ClearButton()
                        .Show());

                LabelRow(paper, "tf_readonly", "ReadOnly", () =>
                    Origami.TextField(paper, "op_tf_ro", _txtReadOnly, v => _txtReadOnly = v)
                        .ReadOnly()
                        .Show());

                LabelRow(paper, "tf_lead", "LeadingIcon", () =>
                    Origami.TextField(paper, "op_tf_lead", _txtLeading, v => _txtLeading = v)
                        .LeadingIcon("@")
                        .Placeholder("user@example.com")
                        .Show());

                LabelRow(paper, "tf_trail", "TrailingIcon (clickable)", () =>
                    Origami.TextField(paper, "op_tf_trail", _txtTrailing, v => _txtTrailing = v)
                        .TrailingIcon("?", () => _txtTrailing = "Help clicked!")
                        .Show());

                LabelRow(paper, "tf_multi", "MultiLine / TextArea (rows: 4)", () =>
                    Origami.TextArea(paper, "op_tf_multi", _txtMulti, v => _txtMulti = v, rows: 4).Show());
            }
        });
    }

    private void Section_TextFieldFilters(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tf_filters", "TextField - filter presets").Body(() =>
        {
            using (paper.Column("op_tf_filters_col").Height(UnitValue.Auto).RowBetween(8).Enter())
            {
                LabelRow(paper, "tf_int", "IntFilter (digits + leading -)", () =>
                    Origami.TextField(paper, "op_tf_int", _txtIntFilter, v => _txtIntFilter = v)
                        .IntFilter()
                        .Placeholder("e.g. -42")
                        .Show());

                LabelRow(paper, "tf_float", "FloatFilter (digits, ., -, e)", () =>
                    Origami.TextField(paper, "op_tf_float", _txtFloatFilter, v => _txtFloatFilter = v)
                        .FloatFilter()
                        .Placeholder("e.g. -3.14e2")
                        .Show());

                LabelRow(paper, "tf_alphanum", "AlphaNumeric", () =>
                    Origami.TextField(paper, "op_tf_alphanum", _txtAlphaNum, v => _txtAlphaNum = v)
                        .AlphaNumeric()
                        .Placeholder("letters and digits only")
                        .Show());

                LabelRow(paper, "tf_nospaces", "NoSpaces", () =>
                    Origami.TextField(paper, "op_tf_nospaces", _txtNoSpaces, v => _txtNoSpaces = v)
                        .NoSpaces()
                        .Placeholder("try pressing space - it's filtered")
                        .Show());
            }
        });
    }

    private void Section_TextFieldValidation(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tf_valid", "TextField - validation / helper").Body(() =>
        {
            using (paper.Column("op_tf_valid_col").Height(UnitValue.Auto).RowBetween(10).Enter())
            {
                LabelRow(paper, "tf_helper", "HelperText (muted hint below)", () =>
                    Origami.TextField(paper, "op_tf_helper", _txtHelper, v => _txtHelper = v)
                        .Placeholder("Username")
                        .HelperText("3-20 characters, letters and digits")
                        .Show());

                LabelRow(paper, "tf_error", "Error (forced)", () =>
                    Origami.TextField(paper, "op_tf_error", _txtError, v => _txtError = v)
                        .Error("This field is invalid right now.")
                        .Show());

                LabelRow(paper, "tf_validator", "Validator (live)", () =>
                    Origami.TextField(paper, "op_tf_validator", _txtValidator, v => _txtValidator = v)
                        .Placeholder("Type at least 5 characters")
                        .Validator(s => (s.Length >= 5, $"At least 5 chars, got {s.Length}"))
                        .Show());
            }
        });
    }

    private void Section_TextFieldAutoComplete(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tf_ac", "TextField - autocomplete").Body(() =>
        {
            using (paper.Column("op_tf_ac_col").Height(UnitValue.Auto).RowBetween(8).Enter())
            {
                LabelRow(paper, "tf_ac", "Country picker (substring filter)", () =>
                    Origami.TextField(paper, "op_tf_ac", _txtAutoComplete, v => _txtAutoComplete = v)
                        .Placeholder("Type a country name...")
                        .AutoComplete(s_countries.Select(c => c.Name).ToArray())
                        .Show());
            }
        });
    }

    // -- NumericField sections ----------------------------------

    private void Section_NumericTypes(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_num_types", "NumericField - every numeric type").Body(() =>
        {
            using (paper.Column("op_num_types_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "n_float",   "float",   () =>
                    Origami.NumericField<float>(paper, "op_n_f",  _numFloat,  v => _numFloat = v).Show());
                LabelRow(paper, "n_double",  "double",  () =>
                    Origami.NumericField<double>(paper, "op_n_d", _numDouble, v => _numDouble = v).Show());
                LabelRow(paper, "n_decimal", "decimal", () =>
                    Origami.NumericField<decimal>(paper, "op_n_dc", _numDecimal, v => _numDecimal = v).Show());
                LabelRow(paper, "n_int",     "int",     () =>
                    Origami.NumericField<int>(paper, "op_n_i",   _numInt,    v => _numInt = v).Show());
                LabelRow(paper, "n_uint",    "uint",    () =>
                    Origami.NumericField<uint>(paper, "op_n_u",  _numUint,   v => _numUint = v).Show());
                LabelRow(paper, "n_long",    "long",    () =>
                    Origami.NumericField<long>(paper, "op_n_l",  _numLong,   v => _numLong = v).Show());
                LabelRow(paper, "n_short",   "short",   () =>
                    Origami.NumericField<short>(paper, "op_n_s", _numShort,  v => _numShort = v).Show());
                LabelRow(paper, "n_byte",    "byte (0-255)", () =>
                    Origami.NumericField<byte>(paper, "op_n_b",  _numByte,   v => _numByte = v).Show());
                LabelRow(paper, "n_sbyte",   "sbyte",   () =>
                    Origami.NumericField<sbyte>(paper, "op_n_sb", _numSbyte, v => _numSbyte = v).Show());
            }
        });
    }

    private void Section_NumericClampStep(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_num_clamp", "NumericField - Min / Max / Step").Body(() =>
        {
            using (paper.Column("op_num_clamp_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "n_clamp", "Min(0) Max(100)", () =>
                    Origami.NumericField<float>(paper, "op_n_clamp", _numClamped, v => _numClamped = v)
                        .Min(0f).Max(100f)
                        .HelperText("Value is clamped to [0, 100]")
                        .Show());

                LabelRow(paper, "n_step", "Step(0.5) Min(-10) Max(10)", () =>
                    Origami.NumericField<float>(paper, "op_n_step", _numStep, v => _numStep = v)
                        .Min(-10f).Max(10f).Step(0.5f)
                        .HelperText("Snaps to multiples of 0.5")
                        .Show());

                LabelRow(paper, "n_fmt", "Format(\"N2\") (thousands + 2 decimals)", () =>
                    Origami.NumericField<float>(paper, "op_n_fmt", _numFormatted, v => _numFormatted = v)
                        .Format("N2")
                        .Show());
            }
        });
    }

    private void Section_NumericCulture(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_num_culture", "NumericField - culture").Body(() =>
        {
            using (paper.Column("op_num_culture_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "n_inv", "InvariantCulture (decimal is always '.')", () =>
                    Origami.NumericField<float>(paper, "op_n_inv", _numInvariant, v => _numInvariant = v)
                        .Culture(System.Globalization.CultureInfo.InvariantCulture)
                        .HelperText("Asset / code-facing values usually want this")
                        .Show());

                LabelRow(paper, "n_de", "de-DE (decimal is ',')", () =>
                    Origami.NumericField<float>(paper, "op_n_de", _numFloat, v => _numFloat = v)
                        .Culture(System.Globalization.CultureInfo.GetCultureInfo("de-DE"))
                        .HelperText("Same backing field as 'float' above")
                        .Show());
            }
        });
    }

    // -- Toggle sections ----------------------------------------

    private void Section_ToggleBasics(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tg_basics", "Toggle - Switch / Checkbox / Radio").Body(() =>
        {
            using (paper.Column("op_tg_basics_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "tg_sw", "Switch (default)", () =>
                    Origami.Switch(paper, "op_tg_sw", _swBasic, v => _swBasic = v)
                        .LabelRight("Enable thing")
                        .Show());

                LabelRow(paper, "tg_cb", "Checkbox", () =>
                    Origami.Checkbox(paper, "op_tg_cb", _cbBasic, v => _cbBasic = v)
                        .LabelRight("I agree")
                        .Show());

                LabelRow(paper, "tg_rd", "Radio (single)", () =>
                    Origami.Radio(paper, "op_tg_rd", _rdSingle, v => _rdSingle = v)
                        .LabelRight("Pick this option")
                        .Show());

                LabelRow(paper, "tg_settings_row", "Settings-row pattern (label + description)", () =>
                    Origami.Switch(paper, "op_tg_settings", _swSettings1, v => _swSettings1 = v)
                        .Primary()
                        .LabelRight("Cloud sync")
                        .Description("Keep your projects in sync across machines")
                        .Stretch()
                        .Show());

                LabelRow(paper, "tg_label_left", "Label on the left (push visual right)", () =>
                    Origami.Switch(paper, "op_tg_lleft", _swLabelLeft, v => _swLabelLeft = v)
                        .Success()
                        .LabelLeft("Notifications")
                        .Stretch()
                        .Show());
            }
        });
    }

    private void Section_ToggleVariants(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tg_var", "Toggle - variants").Body(() =>
        {
            using (paper.Column("op_tg_var_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "v_def", "Default (Primary on-color)", () =>
                    Origami.Switch(paper, "op_tg_v_def", true, _ => { }).LabelRight("Default").Show());
                LabelRow(paper, "v_pri", "Primary", () =>
                    Origami.Switch(paper, "op_tg_v_pri", true, _ => { }).Primary().LabelRight("Primary").Show());
                LabelRow(paper, "v_suc", "Success", () =>
                    Origami.Switch(paper, "op_tg_v_suc", true, _ => { }).Success().LabelRight("Success").Show());
                LabelRow(paper, "v_war", "Warning", () =>
                    Origami.Switch(paper, "op_tg_v_war", true, _ => { }).Warning().LabelRight("Warning").Show());
                LabelRow(paper, "v_dan", "Danger", () =>
                    Origami.Switch(paper, "op_tg_v_dan", _swDanger, v => _swDanger = v).Danger().LabelRight("Danger (live)").Show());
                LabelRow(paper, "v_inf", "Info", () =>
                    Origami.Switch(paper, "op_tg_v_inf", true, _ => { }).Info().LabelRight("Info").Show());
                LabelRow(paper, "v_sub", "Subtle (whisper-quiet)", () =>
                    Origami.Switch(paper, "op_tg_v_sub", true, _ => { }).Subtle().LabelRight("Subtle").Show());

                paper.Box("op_tg_var_div").Height(8).IsNotInteractable();

                LabelRow(paper, "v_cb_pri", "Checkbox - Primary", () =>
                    Origami.Checkbox(paper, "op_tg_cb_pri", true, _ => { }).Primary().LabelRight("Primary").Show());
                LabelRow(paper, "v_cb_suc", "Checkbox - Success", () =>
                    Origami.Checkbox(paper, "op_tg_cb_suc", true, _ => { }).Success().LabelRight("Success").Show());
                LabelRow(paper, "v_cb_dan", "Checkbox - Danger (live)", () =>
                    Origami.Checkbox(paper, "op_tg_cb_dan", _cbDanger, v => _cbDanger = v).Danger().LabelRight("Danger").Show());
            }
        });
    }

    private void Section_ToggleSizes(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tg_sz", "Toggle - sizes").Body(() =>
        {
            using (paper.Column("op_tg_sz_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "sz_sm", "Small (14)", () =>
                    Origami.Switch(paper, "op_tg_sz_sm", _swSmall, v => _swSmall = v)
                        .Primary().Small().LabelRight("Compact").Show());

                LabelRow(paper, "sz_md", "Medium (18 - default)", () =>
                    Origami.Switch(paper, "op_tg_sz_md", _swMedium, v => _swMedium = v)
                        .Primary().Medium().LabelRight("Medium").Show());

                LabelRow(paper, "sz_lg", "Large (24)", () =>
                    Origami.Switch(paper, "op_tg_sz_lg", _swLarge, v => _swLarge = v)
                        .Primary().Large().LabelRight("Hero").Show());

                LabelRow(paper, "sz_cb_md", "Checkbox sizes (S / M / L)", () =>
                {
                    using (paper.Row("op_tg_sz_cb_row").Height(28).RowBetween(12).Enter())
                    {
                        Origami.Checkbox(paper, "op_tg_sz_cb_s", true, _ => { }).Primary().Small().Show();
                        Origami.Checkbox(paper, "op_tg_sz_cb_m", true, _ => { }).Primary().Medium().Show();
                        Origami.Checkbox(paper, "op_tg_sz_cb_l", true, _ => { }).Primary().Large().Show();
                    }
                });
            }
        });
    }

    private void Section_ToggleStates(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tg_state", "Toggle - states (disabled / read-only / errors)").Body(() =>
        {
            using (paper.Column("op_tg_state_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "st_dis", "Disabled (no hover, dimmed)", () =>
                    Origami.Switch(paper, "op_tg_dis", _swDisabled, v => _swDisabled = v)
                        .Primary()
                        .Disabled()
                        .LabelRight("Disabled (still on)")
                        .Show());

                LabelRow(paper, "st_ro", "Read-only (visible but inert)", () =>
                    Origami.Switch(paper, "op_tg_ro", _swReadOnly, v => _swReadOnly = v)
                        .Success()
                        .ReadOnly()
                        .LabelRight("Read-only (still on)")
                        .Show());

                LabelRow(paper, "st_helper", "Helper text", () =>
                    Origami.Switch(paper, "op_tg_help", _swSettings3, v => _swSettings3 = v)
                        .Primary()
                        .LabelRight("Anonymous telemetry")
                        .HelperText("Helps us improve the editor. No PII collected.")
                        .Show());

                LabelRow(paper, "st_err", "Error (forced)", () =>
                    Origami.Checkbox(paper, "op_tg_err", _cbError, v => _cbError = v)
                        .LabelRight("Accept terms")
                        .Error("You must accept the terms to continue")
                        .Show());

                LabelRow(paper, "st_indet", "Checkbox - Indeterminate (tri-state)", () =>
                {
                    int onCount = (_cbA ? 1 : 0) + (_cbB ? 1 : 0) + (_cbC ? 1 : 0);
                    bool? indet = onCount == 0 ? false : onCount == 3 ? true : (bool?)null;
                    using (paper.Column("op_tg_indet_col").Height(UnitValue.Auto).RowBetween(2).Enter())
                    {
                        Origami.Checkbox(paper, "op_tg_indet_all", onCount == 3, v =>
                        {
                            _cbA = _cbB = _cbC = v;
                        })
                            .Primary()
                            .Indeterminate(indet == null ? true : null)
                            .LabelRight("All items")
                            .Show();

                        using (paper.Column("op_tg_indet_kids").Height(UnitValue.Auto).RowBetween(2).Margin(20, 0, 0, 0).Enter())
                        {
                            Origami.Checkbox(paper, "op_tg_indet_a", _cbA, v => _cbA = v).Primary().LabelRight("Item A").Show();
                            Origami.Checkbox(paper, "op_tg_indet_b", _cbB, v => _cbB = v).Primary().LabelRight("Item B").Show();
                            Origami.Checkbox(paper, "op_tg_indet_c", _cbC, v => _cbC = v).Primary().LabelRight("Item C").Show();
                        }
                    }
                });
            }
        });
    }

    private void Section_SwitchExtras(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sw_extras", "Switch - on/off text, glyphs, custom visual").Body(() =>
        {
            using (paper.Column("op_sw_extras_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "sw_onoff", "OnText / OffText", () =>
                    Origami.Switch(paper, "op_sw_onoff", _swOnOffText, v => _swOnOffText = v)
                        .Primary()
                        .Large()
                        .OnText("ON").OffText("OFF")
                        .LabelRight("Power")
                        .Show());

                LabelRow(paper, "sw_yesno", "Yes / No (Success variant)", () =>
                    Origami.Switch(paper, "op_sw_yesno", _swYesNo, v => _swYesNo = v)
                        .Success()
                        .Large()
                        .OnText("YES").OffText("NO")
                        .LabelRight("Confirm")
                        .Show());

                LabelRow(paper, "sw_glyph", "Glyph inside knob (check / x)", () =>
                    Origami.Switch(paper, "op_sw_glyph", _swGlyph, v => _swGlyph = v)
                        .Primary()
                        .Large()
                        .OnGlyph("✓").OffGlyph("✕")
                        .LabelRight("Auto-save")
                        .Show());

                LabelRow(paper, "sw_custom", "CustomVisual (caller-drawn)", () =>
                    Origami.Switch(paper, "op_sw_custom", _swCustom, v => _swCustom = v)
                        .LabelRight("Bespoke visual")
                        .CustomVisual(ctx =>
                        {
                            // Draw a vertical bar that fills bottom-up as it turns on.
                            float h = ctx.Size;
                            float fillH = h * ctx.AnimationT;
                            paper.Box("cust_bg")
                                .Width(h * 0.6f).Height(h)
                                .BackgroundColor(ctx.Theme.Neutral.C300)
                                .Rounded(2)
                                .IsNotInteractable();
                            if (fillH > 0.5f)
                            {
                                paper.Box("cust_fill")
                                    .PositionType(PositionType.SelfDirected)
                                    .Position(0, h - fillH)
                                    .Width(h * 0.6f).Height(fillH)
                                    .BackgroundColor(ctx.Theme.Primary.C500)
                                    .Rounded(2)
                                    .IsNotInteractable();
                            }
                        })
                        .Show());
            }
        });
    }

    private void Section_CheckboxExtras(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_cb_extras", "Checkbox - helper / disabled list").Body(() =>
        {
            using (paper.Column("op_cb_extras_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "cb_helper", "Helper text under row", () =>
                    Origami.Checkbox(paper, "op_cb_helper", _cbHelper, v => _cbHelper = v)
                        .Primary()
                        .LabelRight("Subscribe to newsletter")
                        .HelperText("We send one short email per month.")
                        .Show());

                LabelRow(paper, "cb_disabled", "Disabled (off + on)", () =>
                {
                    using (paper.Column("op_cb_dis_col").Height(UnitValue.Auto).RowBetween(2).Enter())
                    {
                        Origami.Checkbox(paper, "op_cb_dis_off", false, _ => { })
                            .Primary().Disabled().LabelRight("Disabled (off)").Show();
                        Origami.Checkbox(paper, "op_cb_dis_on", true, _ => { })
                            .Primary().Disabled().LabelRight("Disabled (on)").Show();
                    }
                });
            }
        });
    }

    private void Section_RadioGroups(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_rg", "Radio groups").Body(() =>
        {
            using (paper.Column("op_rg_col").Height(UnitValue.Auto).RowBetween(8).Enter())
            {
                LabelRow(paper, "rg_v", "Vertical EnumRadioGroup with descriptions", () =>
                    Origami.EnumRadioGroup(paper, "op_rg_ship", _rgShip, v => _rgShip = v)
                        .Primary()
                        .Description(m => m switch
                        {
                            ShipMode.Standard => "5-7 business days. Free.",
                            ShipMode.Express => "2-3 business days. $9.99.",
                            ShipMode.Overnight => "Next business day. $24.99.",
                            _ => null,
                        })
                        .Show());

                LabelRow(paper, "rg_h", "Horizontal RadioGroup", () =>
                    Origami.EnumRadioGroup(paper, "op_rg_ship_h", _rgShipH, v => _rgShipH = v)
                        .Success()
                        .Horizontal()
                        .Gap(16)
                        .Show());

                LabelRow(paper, "rg_theme", "Theme picker (typed)", () =>
                    Origami.RadioGroup(paper, "op_rg_theme", _rgTheme, v => _rgTheme = v,
                        new[] { Theme.System, Theme.Light, Theme.Dark })
                        .Info()
                        .Display(t => t.ToString())
                        .Description(t => t switch
                        {
                            Theme.System => "Follow OS preference",
                            Theme.Light => "Always light",
                            Theme.Dark => "Always dark",
                            _ => null,
                        })
                        .Show());
            }
        });
    }

    // -- Slider sections ----------------------------------------

    private void Section_SliderBasics(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_basics", "Slider - basics").Body(() =>
        {
            using (paper.Column("op_sl_b_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "sl_b_float", "Float (0..1)", () =>
                    Origami.Slider(paper, "op_sl_b_f", _slBasic, v => _slBasic = v, 0f, 1f).Format("F2").Show());

                LabelRow(paper, "sl_b_int", "IntSlider (0..100)", () =>
                    Origami.IntSlider(paper, "op_sl_b_i", _slIntBasic, v => _slIntBasic = v, 0, 100).Show());

                LabelRow(paper, "sl_b_step", "Float w/ Step(0.5)", () =>
                    Origami.Slider(paper, "op_sl_b_step", _slStep, v => _slStep = v, 0f, 10f)
                        .Step(0.5f).Format("F1").Show());

                LabelRow(paper, "sl_b_novalue", "ShowValue(false)", () =>
                    Origami.Slider(paper, "op_sl_b_nv", _slNoValue, v => _slNoValue = v, 0f, 1f)
                        .ShowValue(false).Show());

                LabelRow(paper, "sl_b_widefmt", "Format(\"N2\") wide-range", () =>
                    Origami.Slider(paper, "op_sl_b_wfmt", _slWideFmt, v => _slWideFmt = v, 0f, 5000f)
                        .Format("N2").Show());
            }
        });
    }

    private void Section_SliderVariants(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_var", "Slider - variants").Body(() =>
        {
            using (paper.Column("op_sl_v_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "v_def", "Default (Primary on-color)", () =>
                    Origami.Slider(paper, "op_sl_v_def", _slBasic, v => _slBasic = v, 0f, 1f).Format("F2").Show());
                LabelRow(paper, "v_pri", "Primary", () =>
                    Origami.Slider(paper, "op_sl_v_pri", _slPrimary, v => _slPrimary = v, 0f, 1f).Primary().Format("F2").Show());
                LabelRow(paper, "v_suc", "Success", () =>
                    Origami.Slider(paper, "op_sl_v_suc", _slSuccess, v => _slSuccess = v, 0f, 1f).Success().Format("F2").Show());
                LabelRow(paper, "v_war", "Warning", () =>
                    Origami.Slider(paper, "op_sl_v_war", _slWarning, v => _slWarning = v, 0f, 1f).Warning().Format("F2").Show());
                LabelRow(paper, "v_dan", "Danger", () =>
                    Origami.Slider(paper, "op_sl_v_dan", _slDanger, v => _slDanger = v, 0f, 1f).Danger().Format("F2").Show());
                LabelRow(paper, "v_inf", "Info", () =>
                    Origami.Slider(paper, "op_sl_v_inf", _slInfo, v => _slInfo = v, 0f, 1f).Info().Format("F2").Show());
                LabelRow(paper, "v_sub", "Subtle", () =>
                    Origami.Slider(paper, "op_sl_v_sub", _slSubtle, v => _slSubtle = v, 0f, 1f).Subtle().Format("F2").Show());
            }
        });
    }

    private void Section_SliderSizes(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_sz", "Slider - sizes").Body(() =>
        {
            using (paper.Column("op_sl_sz_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "sz_sm", "Small (20)", () =>
                    Origami.Slider(paper, "op_sl_sz_sm", _slSmall, v => _slSmall = v, 0f, 1f).Primary().Small().Format("F2").Show());
                LabelRow(paper, "sz_md", "Medium (24 - default)", () =>
                    Origami.Slider(paper, "op_sl_sz_md", _slMedium, v => _slMedium = v, 0f, 1f).Primary().Medium().Format("F2").Show());
                LabelRow(paper, "sz_lg", "Large (32)", () =>
                    Origami.Slider(paper, "op_sl_sz_lg", _slLarge, v => _slLarge = v, 0f, 1f).Primary().Large().Format("F2").Show());
            }
        });
    }

    private void Section_SliderScale(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_scale", "Slider - log + bipolar").Body(() =>
        {
            using (paper.Column("op_sl_sc_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "sc_log", "Logarithmic (1..10000)", () =>
                    Origami.Slider(paper, "op_sl_sc_log", _slLog, v => _slLog = v, 1f, 10000f)
                        .Logarithmic()
                        .Format("F1")
                        .HelperText("Log scale gives perceptually-linear control over wide ranges")
                        .Show());

                LabelRow(paper, "sc_bipolar", "Bipolar (-1..1, fills from 0)", () =>
                    Origami.Slider(paper, "op_sl_sc_bp", _slBipolar, v => _slBipolar = v, -1f, 1f)
                        .Bipolar().Format("F2").Show());

                LabelRow(paper, "sc_contrast", "Bipolar w/ Variant + Format", () =>
                    Origami.Slider(paper, "op_sl_sc_ct", _slContrast, v => _slContrast = v, -100f, 100f)
                        .Bipolar()
                        .Primary()
                        .Format("+0;-0;0")
                        .HelperText("Centered ranges (contrast / saturation / EV) read better with bipolar fill")
                        .Show());
            }
        });
    }

    private void Section_SliderTicks(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_ticks", "Slider - ticks + labels").Body(() =>
        {
            using (paper.Column("op_sl_tk_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "tk_simple", "Ticks(11) - no labels", () =>
                    Origami.Slider(paper, "op_sl_tk_s", _slTicks, v => _slTicks = v, 0f, 100f)
                        .Ticks(11).Step(10f).Format("F0").Show());

                LabelRow(paper, "tk_labels", "Ticks(5) + TickLabels", () =>
                    Origami.Slider(paper, "op_sl_tk_l", _slTickLabels, v => _slTickLabels = v, 0f, 100f)
                        .Ticks(5)
                        .TickLabels((i, v) => $"{(int)v}")
                        .Step(25f)
                        .Format("F0")
                        .Show());
            }
        });
    }

    private void Section_SliderStates(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_state", "Slider - states").Body(() =>
        {
            using (paper.Column("op_sl_st_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "st_disabled", "Disabled", () =>
                    Origami.Slider(paper, "op_sl_st_dis", _slDisabled, v => _slDisabled = v, 0f, 1f)
                        .Primary().Disabled().Format("F2").Show());

                LabelRow(paper, "st_readonly", "ReadOnly", () =>
                    Origami.Slider(paper, "op_sl_st_ro", _slReadOnly, v => _slReadOnly = v, 0f, 1f)
                        .Success().ReadOnly().Format("F2").Show());

                LabelRow(paper, "st_helper", "HelperText", () =>
                    Origami.Slider(paper, "op_sl_st_help", _slBasic, v => _slBasic = v, 0f, 1f)
                        .Primary()
                        .HelperText("Tip: hold Ctrl while dragging or using arrows for 10x precision, Shift for 0.1x")
                        .Format("F2")
                        .Show());

                LabelRow(paper, "st_error", "Error message", () =>
                    Origami.Slider(paper, "op_sl_st_err", _slErr, v => _slErr = v, 0f, 1f)
                        .Format("F2")
                        .Error("Forced error - shows the danger ramp on the helper line")
                        .Show());

                LabelRow(paper, "st_validator", "Validator (must be > 0.5)", () =>
                    Origami.Slider(paper, "op_sl_st_val", _slBasic, v => _slBasic = v, 0f, 1f)
                        .Format("F2")
                        .Validator(v => v > 0.5f ? (true, null) : (false, "Drag the thumb past the midpoint"))
                        .Show());
            }
        });
    }

    private void Section_SliderExtras(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_xtra", "Slider - tooltip / drag hooks / custom render").Body(() =>
        {
            using (paper.Column("op_sl_xt_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "xt_tooltip_off", "ShowTooltip(false)", () =>
                    Origami.Slider(paper, "op_sl_xt_tt", _slBasic, v => _slBasic = v, 0f, 1f)
                        .Primary().Format("F2").ShowTooltip(false).Show());

                LabelRow(paper, "xt_drag_hooks", $"OnDragStart / OnDragEnd  (start: {_slDragCount}, end: {_slDragEndCount})", () =>
                    Origami.Slider(paper, "op_sl_xt_dh", _slBasic, v => _slBasic = v, 0f, 1f)
                        .Primary().Format("F2")
                        .OnDragStart(() => _slDragCount++)
                        .OnDragEnd(() => _slDragEndCount++)
                        .Show());

                LabelRow(paper, "xt_no_snap_during_drag", "SnapWhileDragging(false) + Step(0.1)", () =>
                    Origami.Slider(paper, "op_sl_xt_snap", _slBasic, v => _slBasic = v, 0f, 1f)
                        .Primary().Step(0.1f).SnapWhileDragging(false).Format("F2").Show());

                LabelRow(paper, "xt_custom_thumb", "CustomThumb (caller-drawn)", () =>
                    Origami.Slider(paper, "op_sl_xt_cust", _slCustom, v => _slCustom = v, 0f, 1f)
                        .Primary().Format("F2")
                        .CustomThumb((canvas, ctx) =>
                        {
                            // Diamond thumb instead of a circle.
                            float r = ctx.Radius;
                            canvas.SetFillColor(ctx.IsActive ? ctx.Surface.C500 : ctx.Ink.C500);
                            canvas.BeginPath();
                            canvas.MoveTo((float)ctx.Center.X, (float)ctx.Center.Y - r);
                            canvas.LineTo((float)ctx.Center.X + r, (float)ctx.Center.Y);
                            canvas.LineTo((float)ctx.Center.X, (float)ctx.Center.Y + r);
                            canvas.LineTo((float)ctx.Center.X - r, (float)ctx.Center.Y);
                            canvas.Fill();
                        })
                        .Show());
            }
        });
    }

    private void Section_SliderVertical(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_sl_vert", "Slider - vertical").Body(() =>
        {
            using (paper.Row("op_sl_vt_row").Height(160).RowBetween(20).Enter())
            {
                using (paper.Column("op_sl_vt_a").Width(80).Height(160).Enter())
                {
                    Origami.Slider(paper, "op_sl_vt_a_v", _slVert, v => _slVert = v, 0f, 1f)
                        .Primary().Vertical().ShowValue(false).Format("F2")
                        .Width(40f).Height(140f)
                        .Show();
                    paper.Box("op_sl_vt_a_lbl").Width(80).Height(20)
                        .Alignment(TextAlignment.MiddleCenter).IsNotInteractable()
                        .Text($"{_slVert:F2}", EditorTheme.DefaultFont)
                        .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 2);
                }

                using (paper.Column("op_sl_vt_b").Width(80).Height(160).Enter())
                {
                    Origami.Slider(paper, "op_sl_vt_b_v", _slBipolar, v => _slBipolar = v, -1f, 1f)
                        .Success().Vertical().Bipolar().ShowValue(false).Format("F2")
                        .Width(40f).Height(140f)
                        .Show();
                    paper.Box("op_sl_vt_b_lbl").Width(80).Height(20)
                        .Alignment(TextAlignment.MiddleCenter).IsNotInteractable()
                        .Text($"bipolar {_slBipolar:F2}", EditorTheme.DefaultFont)
                        .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 2);
                }
            }
        });
    }

    private void Section_RangeSliderShowcase(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_rs", "RangeSlider").Body(() =>
        {
            using (paper.Column("op_rs_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "rs_basic", "Float (0..1)", () =>
                    Origami.RangeSlider(paper, "op_rs_b", _rsLow, _rsHigh,
                        (lo, hi) => { _rsLow = lo; _rsHigh = hi; }, 0f, 1f).Format("F2").Show());

                LabelRow(paper, "rs_int", "IntRangeSlider (0..100)", () =>
                    Origami.IntRangeSlider(paper, "op_rs_i", _rsIntLow, _rsIntHigh,
                        (lo, hi) => { _rsIntLow = lo; _rsIntHigh = hi; }, 0, 100).Show());

                LabelRow(paper, "rs_step", "Step(10) + Variant", () =>
                    Origami.RangeSlider(paper, "op_rs_s", _rsStepLow, _rsStepHigh,
                        (lo, hi) => { _rsStepLow = lo; _rsStepHigh = hi; }, 0f, 100f)
                        .Primary().Step(10f).Format("F0").Show());

                LabelRow(paper, "rs_mindist", "MinDistance(20)", () =>
                    Origami.RangeSlider(paper, "op_rs_md", _rsMinDistLow, _rsMinDistHigh,
                        (lo, hi) => { _rsMinDistLow = lo; _rsMinDistHigh = hi; }, 0f, 100f)
                        .Success().MinDistance(20f).Format("F0")
                        .HelperText("Thumbs always stay 20 apart")
                        .Show());

                LabelRow(paper, "rs_noswap", "AllowSwap(false)", () =>
                    Origami.RangeSlider(paper, "op_rs_ns", _rsNoSwapLow, _rsNoSwapHigh,
                        (lo, hi) => { _rsNoSwapLow = lo; _rsNoSwapHigh = hi; }, 0f, 100f)
                        .Warning().AllowSwap(false).Format("F0")
                        .HelperText("Drag low past high - clamps instead of swapping")
                        .Show());

                LabelRow(paper, "rs_ticks", "Ticks(5) + TickLabels", () =>
                    Origami.RangeSlider(paper, "op_rs_tk", _rsTickLow, _rsTickHigh,
                        (lo, hi) => { _rsTickLow = lo; _rsTickHigh = hi; }, 0f, 100f)
                        .Info().Ticks(5).TickLabels((i, v) => $"{(int)v}").Format("F0").Show());
            }
        });
    }

    // -- Button showcase ----------------------------------------

    private void Section_Buttons(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_buttons", "Buttons").Body(() =>
        {
            using (paper.Column("op_btn_root").Height(UnitValue.Auto).RowBetween(4).Enter())
            {
                ButtonsSection_Basics(paper);
                ButtonsSection_Variants(paper);
                ButtonsSection_Styles(paper);
                ButtonsSection_Sizes(paper);
                ButtonsSection_Width(paper);
                ButtonsSection_Icons(paper);
                ButtonsSection_States(paper);
                ButtonsSection_ClickKinds(paper);
                ButtonsSection_VisualExtras(paper);
                ButtonsSection_Custom(paper);
                ButtonsSection_Group(paper);
            }
        });
    }

    private void ButtonsSection_Basics(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_basics", "Basics").Body(() =>
        {
            using (paper.Column("op_btn_b_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "btn_b_default", $"Default click counter ({_btnClickCount})", () =>
                    Origami.Button(paper, "op_btn_b_def", "Click me", () => _btnClickCount++).Show());

                LabelRow(paper, "btn_b_primary", "Primary CTA", () =>
                    Origami.Button(paper, "op_btn_b_pri", "Save Changes", () => _btnClickCount++).Primary().Show());

                LabelRow(paper, "btn_b_chained", "Chained variant + style", () =>
                    Origami.Button(paper, "op_btn_b_chain", "Confirm", () => _btnClickCount++)
                        .Success().Filled().Show());
            }
        });
    }

    private void ButtonsSection_Variants(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_var", "Variants").Body(() =>
        {
            using (paper.Column("op_btn_v_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "v_def", "Default", () =>
                    Origami.Button(paper, "op_btn_v_def", "Default", () => _btnClickCount++).Show());
                LabelRow(paper, "v_pri", "Primary", () =>
                    Origami.Button(paper, "op_btn_v_pri", "Primary", () => _btnClickCount++).Primary().Show());
                LabelRow(paper, "v_suc", "Success", () =>
                    Origami.Button(paper, "op_btn_v_suc", "Success", () => _btnClickCount++).Success().Show());
                LabelRow(paper, "v_war", "Warning", () =>
                    Origami.Button(paper, "op_btn_v_war", "Warning", () => _btnClickCount++).Warning().Show());
                LabelRow(paper, "v_dan", "Danger", () =>
                    Origami.Button(paper, "op_btn_v_dan", "Delete", () => _btnClickCount++).Danger().Show());
                LabelRow(paper, "v_inf", "Info", () =>
                    Origami.Button(paper, "op_btn_v_inf", "Learn more", () => _btnClickCount++).Info().Show());
                LabelRow(paper, "v_sub", "Subtle", () =>
                    Origami.Button(paper, "op_btn_v_sub", "Subtle", () => _btnClickCount++).Subtle().Show());
            }
        });
    }

    private void ButtonsSection_Styles(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_styles", "Styles").Body(() =>
        {
            using (paper.Column("op_btn_st_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "st_filled", "Filled (default)", () =>
                    Origami.Button(paper, "op_btn_st_fil", "Filled", () => _btnClickCount++).Primary().Filled().Show());

                LabelRow(paper, "st_outline", "Outline", () =>
                    Origami.Button(paper, "op_btn_st_out", "Outline", () => _btnClickCount++).Primary().Outline().Show());

                LabelRow(paper, "st_ghost", "Ghost", () =>
                    Origami.Button(paper, "op_btn_st_gho", "Ghost", () => _btnClickCount++).Primary().Ghost().Show());

                LabelRow(paper, "st_soft", "Soft", () =>
                    Origami.Button(paper, "op_btn_st_sof", "Soft", () => _btnClickCount++).Primary().Soft().Show());

                LabelRow(paper, "st_link", "Link", () =>
                    Origami.Button(paper, "op_btn_st_lnk", "Open documentation", () => _btnClickCount++).Primary().Link().Show());

                LabelRow(paper, "st_row_all", "Side-by-side (Danger)", () =>
                {
                    using (paper.Row("op_btn_st_row").Height(UnitValue.Auto).RowBetween(8).Enter())
                    {
                        Origami.Button(paper, "op_btn_st_row_f", "Filled", () => _btnClickCount++).Danger().Filled().Show();
                        Origami.Button(paper, "op_btn_st_row_o", "Outline", () => _btnClickCount++).Danger().Outline().Show();
                        Origami.Button(paper, "op_btn_st_row_g", "Ghost", () => _btnClickCount++).Danger().Ghost().Show();
                        Origami.Button(paper, "op_btn_st_row_s", "Soft", () => _btnClickCount++).Danger().Soft().Show();
                        Origami.Button(paper, "op_btn_st_row_l", "Link", () => _btnClickCount++).Danger().Link().Show();
                    }
                });
            }
        });
    }

    private void ButtonsSection_Sizes(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_sz", "Sizes").Body(() =>
        {
            using (paper.Column("op_btn_sz_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "sz_sm", "Small", () =>
                    Origami.Button(paper, "op_btn_sz_sm", "Compact", () => _btnClickCount++).Primary().Small().Show());
                LabelRow(paper, "sz_md", "Medium (default)", () =>
                    Origami.Button(paper, "op_btn_sz_md", "Standard", () => _btnClickCount++).Primary().Medium().Show());
                LabelRow(paper, "sz_lg", "Large", () =>
                    Origami.Button(paper, "op_btn_sz_lg", "Hero", () => _btnClickCount++).Primary().Large().Show());

                LabelRow(paper, "sz_row_all", "Side-by-side", () =>
                {
                    using (paper.Row("op_btn_sz_row").Height(UnitValue.Auto).RowBetween(8).Enter())
                    {
                        Origami.Button(paper, "op_btn_sz_row_s", "S", () => _btnClickCount++).Primary().Small().Show();
                        Origami.Button(paper, "op_btn_sz_row_m", "M", () => _btnClickCount++).Primary().Medium().Show();
                        Origami.Button(paper, "op_btn_sz_row_l", "L", () => _btnClickCount++).Primary().Large().Show();
                    }
                });
            }
        });
    }

    private void ButtonsSection_Width(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_w", "Width / Layout").Body(() =>
        {
            using (paper.Column("op_btn_w_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "w_fit", "FitContent (default)", () =>
                    Origami.Button(paper, "op_btn_w_fit", "Hugs label", () => _btnClickCount++).Primary().FitContent().Show());

                LabelRow(paper, "w_120", "Width(120)", () =>
                    Origami.Button(paper, "op_btn_w_120", "Fixed", () => _btnClickCount++).Primary().Width(120f).Show());

                LabelRow(paper, "w_full", "FullWidth", () =>
                    Origami.Button(paper, "op_btn_w_full", "Stretch to row", () => _btnClickCount++).Primary().FullWidth().Show());

                LabelRow(paper, "w_round", "Custom Rounding(12)", () =>
                    Origami.Button(paper, "op_btn_w_round", "Pill", () => _btnClickCount++).Primary().Width(140f).Rounding(12).Show());
            }
        });
    }

    private void ButtonsSection_Icons(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_ic", "Icons").Body(() =>
        {
            using (paper.Column("op_btn_ic_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "ic_lead", "LeadingIcon", () =>
                    Origami.Button(paper, "op_btn_ic_lead", "Save", () => _btnClickCount++)
                        .Primary().LeadingIcon(EditorIcons.FloppyDisk).Show());

                LabelRow(paper, "ic_trail", "TrailingIcon", () =>
                    Origami.Button(paper, "op_btn_ic_trail", "Continue", () => _btnClickCount++)
                        .Primary().TrailingIcon(EditorIcons.ArrowRight).Show());

                LabelRow(paper, "ic_both", "Leading + Trailing", () =>
                    Origami.Button(paper, "op_btn_ic_both", "Settings", () => _btnClickCount++)
                        .Primary().LeadingIcon(EditorIcons.Gear).TrailingIcon(EditorIcons.ChevronDown).Show());

                LabelRow(paper, "ic_only", "IconButton (square)", () =>
                {
                    using (paper.Row("op_btn_ic_only_row").Height(UnitValue.Auto).RowBetween(6).Enter())
                    {
                        Origami.IconButton(paper, "op_btn_ic_only_a", EditorIcons.Plus, () => _btnClickCount++).Primary().Show();
                        Origami.IconButton(paper, "op_btn_ic_only_b", EditorIcons.Trash, () => _btnClickCount++).Danger().Show();
                        Origami.IconButton(paper, "op_btn_ic_only_c", EditorIcons.Pencil, () => _btnClickCount++).Outline().Show();
                        Origami.IconButton(paper, "op_btn_ic_only_d", EditorIcons.Gear, () => _btnClickCount++).Ghost().Show();
                    }
                });
            }
        });
    }

    private void ButtonsSection_States(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_state", "States").Body(() =>
        {
            using (paper.Column("op_btn_state_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "st_disabled", $"Disabled (toggle below)", () =>
                    Origami.Button(paper, "op_btn_st_dis", "Disabled when checked", () => _btnClickCount++)
                        .Primary().Disabled(_btnDisabled).Show());

                paper.Box("op_btn_st_dis_tog_row").Margin(0, 0, 0, 4).Height(EditorTheme.RowHeight);
                LabelRow(paper, "st_dis_tog", "  Disable toggle", () =>
                    Origami.Switch(paper, "op_btn_st_dis_tog", _btnDisabled, v => _btnDisabled = v).Show());

                LabelRow(paper, "st_loading", "Loading", () =>
                    Origami.Button(paper, "op_btn_st_load", "Importing assets", () => _btnClickCount++)
                        .Primary().Loading(_btnLoading).LeadingIcon(EditorIcons.FloppyDisk).Show());
                LabelRow(paper, "st_load_tog", "  Loading toggle", () =>
                    Origami.Switch(paper, "op_btn_st_load_tog", _btnLoading, v => _btnLoading = v).Show());

                LabelRow(paper, "st_tooltip", "Tooltip on hover", () =>
                    Origami.Button(paper, "op_btn_st_tip", "Hover me", () => _btnClickCount++)
                        .Primary().Tooltip("This is a contextual tooltip - fades in 16ms, lives on Layer.Topmost").Show());

                LabelRow(paper, "st_autofocus", "AutoFocus on first frame", () =>
                    Origami.Button(paper, "op_btn_st_af_dyn_" + (_btnClickCount % 5), "Re-render to focus", () => _btnClickCount++)
                        .Primary().AutoFocus().Show());
            }
        });
    }

    private void ButtonsSection_ClickKinds(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_clicks", "Click kinds").Body(() =>
        {
            using (paper.Column("op_btn_cl_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "cl_basic", $"Click ({_btnClickCount})", () =>
                    Origami.Button(paper, "op_btn_cl_b", "Click", () => _btnClickCount++).Primary().Show());

                LabelRow(paper, "cl_right", $"OnRightClick ({_btnRightClickCount})", () =>
                    Origami.Button(paper, "op_btn_cl_r", "Right-click me", () => _btnClickCount++)
                        .Primary().OnRightClick(() => _btnRightClickCount++).Show());

                LabelRow(paper, "cl_double", $"OnDoubleClick ({_btnDoubleClickCount})", () =>
                    Origami.Button(paper, "op_btn_cl_d", "Double-click me", () => _btnClickCount++)
                        .Primary().OnDoubleClick(() => _btnDoubleClickCount++).Show());

                LabelRow(paper, "cl_all", "All three click handlers", () =>
                    Origami.Button(paper, "op_btn_cl_all", "Click / Right / Double", () => _btnClickCount++)
                        .Primary()
                        .OnRightClick(() => _btnRightClickCount++)
                        .OnDoubleClick(() => _btnDoubleClickCount++)
                        .Show());
            }
        });
    }

    private void ButtonsSection_VisualExtras(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_vx", "Visual extras").Body(() =>
        {
            using (paper.Column("op_btn_vx_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "vx_shadow", "Shadow toggle", () =>
                    Origami.Button(paper, "op_btn_vx_shd", "Has Shadow", () => _btnClickCount++)
                        .Primary().Shadow(_btnShadow).Show());
                LabelRow(paper, "vx_shadow_tog", "  Shadow toggle", () =>
                    Origami.Switch(paper, "op_btn_vx_shd_tog", _btnShadow, v => _btnShadow = v).Show());

                LabelRow(paper, "vx_pulse", "Pulse (CTA)", () =>
                    Origami.Button(paper, "op_btn_vx_pul", "Subscribe", () => _btnClickCount++)
                        .Primary().Pulse(_btnPulse).Show());
                LabelRow(paper, "vx_pulse_tog", "  Pulse toggle", () =>
                    Origami.Switch(paper, "op_btn_vx_pul_tog", _btnPulse, v => _btnPulse = v).Show());
            }
        });
    }

    private void ButtonsSection_Custom(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_cus", "Custom rendering").Body(() =>
        {
            using (paper.Column("op_btn_cu_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "cu_render", "CustomRender (caller paints)", () =>
                    Origami.Button(paper, "op_btn_cu_r", "ignored", () => _btnClickCount++)
                        .Width(160f).Height(34f)
                        .CustomRender((canvas, ctx) =>
                        {
                            float x = (float)ctx.Rect.Min.X;
                            float y = (float)ctx.Rect.Min.Y;
                            float w = (float)ctx.Rect.Size.X;
                            float h = (float)ctx.Rect.Size.Y;
                            float t = ctx.HoverT;
                            // Diagonal gradient - paint two halves with a bevel.
                            var c1 = ctx.Theme.Primary.C500;
                            var c2 = ctx.Theme.Blue.C500;
                            var top = OrigamiRamp.LerpColor(c1, c2, t);
                            var bot = OrigamiRamp.LerpColor(c2, c1, t);
                            canvas.RoundedRectFilled(x, y, w, h * 0.5f, ctx.Theme.Metrics.Rounding, top);
                            canvas.RoundedRectFilled(x, y + h * 0.5f, w, h * 0.5f, ctx.Theme.Metrics.Rounding, bot);
                            if (ctx.Theme.Font != null)
                            {
                                var ts = canvas.MeasureText("Custom", ctx.Theme.Metrics.FontSize, ctx.Theme.Font);
                                canvas.DrawText("Custom",
                                    x + (w - (float)ts.X) * 0.5f,
                                    y + (h - (float)ts.Y) * 0.5f,
                                    ctx.Ink.C700, ctx.Theme.Metrics.FontSize, ctx.Theme.Font);
                            }
                        }).Show());

                LabelRow(paper, "cu_content", "CustomContent (caller layout)", () =>
                    Origami.Button(paper, "op_btn_cu_c", string.Empty, () => _btnClickCount++)
                        .Primary().Width(180f)
                        .CustomContent(() =>
                        {
                            using (paper.Row("op_btn_cu_c_row").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                                .ChildLeft(8).ChildRight(8).RowBetween(6)
                                .Alignment(TextAlignment.MiddleLeft).Enter())
                            {
                                paper.Box("op_btn_cu_c_lbl").Width(UnitValue.Stretch())
                                    .Text("3 unread", EditorTheme.DefaultFont)
                                    .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize)
                                    .Alignment(TextAlignment.MiddleLeft);
                                paper.Box("op_btn_cu_c_pill").Width(28).Height(18)
                                    .BackgroundColor(EditorTheme.Purple400).Rounded(9)
                                    .Text("3", EditorTheme.DefaultFont).TextColor(EditorTheme.Ink500)
                                    .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);
                            }
                        }).Show());
            }
        });
    }

    private static readonly string[] s_bgViewLabels = { "Shaded", "Wireframe", "SDF" };
    private static readonly string[] s_bgAlignLabels = { "Left", "Center", "Right" };

    private void ButtonsSection_Group(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_btn_group", "ButtonGroup (segmented)").Body(() =>
        {
            using (paper.Column("op_btn_g_col").Height(UnitValue.Auto).RowBetween(6).Enter())
            {
                LabelRow(paper, "g_basic", $"Basic (selected: {s_bgViewLabels[_bgViewMode]})", () =>
                    Origami.ButtonGroup(paper, "op_btn_g_b", _bgViewMode, v => _bgViewMode = v)
                        .Primary()
                        .Item("Shaded")
                        .Item("Wireframe")
                        .Item("SDF")
                        .Show());

                LabelRow(paper, "g_icons", $"With icons + tooltips ({s_bgAlignLabels[_bgAlign]})", () =>
                    Origami.ButtonGroup(paper, "op_btn_g_ic", _bgAlign, v => _bgAlign = v)
                        .Success()
                        .Item("Left",   EditorIcons.AlignLeft,   "Align left")
                        .Item("Center", EditorIcons.AlignCenter, "Align center")
                        .Item("Right",  EditorIcons.AlignRight,  "Align right")
                        .Show());

                LabelRow(paper, "g_full", "FullWidth", () =>
                    Origami.ButtonGroup(paper, "op_btn_g_full", _bgViewMode, v => _bgViewMode = v)
                        .Info().FullWidth()
                        .Item("Tab A").Item("Tab B").Item("Tab C")
                        .Show());

                LabelRow(paper, "g_sm", "Small", () =>
                    Origami.ButtonGroup(paper, "op_btn_g_sm", _bgSize, v => _bgSize = v)
                        .Subtle().Small()
                        .Item("XS").Item("S").Item("M").Item("L")
                        .Show());

                LabelRow(paper, "g_lg", "Large + Variants", () =>
                    Origami.ButtonGroup(paper, "op_btn_g_lg", _bgVariant, v => _bgVariant = v)
                        .Danger().Large()
                        .Item("Cancel").Item("Discard").Item("Save")
                        .Show());

                LabelRow(paper, "g_disabled", "DisabledItem", () =>
                    Origami.ButtonGroup(paper, "op_btn_g_dis", _bgViewMode, v => _bgViewMode = v)
                        .Warning()
                        .Item("Available")
                        .DisabledItem("Locked")
                        .Item("Available")
                        .Show());
            }
        });
    }

    private void Section_Tree(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tree", "Tree View").DefaultExpanded().Body(() =>
        {
            using (paper.Column("op_tree_col").Height(UnitValue.Auto).RowBetween(12).Enter())
            {
                var folderColor = Color.FromArgb(255, 220, 180, 80);

                // ---- 1. Basic selection tree (no checkboxes) ----
                StateLine(paper, "op_tree_h1", "Basic - selection, icons, badges");

                var basicNodes = new List<TreeNode>
                {
                    new() { Id = "b_scene", Label = "Scene", Icon = EditorIcons.Film, HasChildren = true, Depth = 0 },
                      new() { Id = "b_cam", Label = "Main Camera", Icon = EditorIcons.Camera, Depth = 1, Badge = "Active" },
                      new() { Id = "b_light", Label = "Directional Light", Icon = EditorIcons.Sun, Depth = 1 },
                      new() { Id = "b_env", Label = "Environment", Icon = EditorIcons.Cube, HasChildren = true, Depth = 1 },
                        new() { Id = "b_terrain", Label = "Terrain", Icon = EditorIcons.Mountain, Depth = 2 },
                        new() { Id = "b_water", Label = "Water Plane", Icon = EditorIcons.Water, Depth = 2 },
                      new() { Id = "b_player", Label = "Player", Icon = EditorIcons.User, HasChildren = true, Depth = 1 },
                        new() { Id = "b_mesh", Label = "Mesh", Icon = EditorIcons.VectorSquare, Depth = 2 },
                        new() { Id = "b_collider", Label = "Collider", Icon = EditorIcons.Cubes, Depth = 2 },
                      new() { Id = "b_ui", Label = "UI Canvas", Icon = EditorIcons.Display, HasChildren = true, Depth = 1 },
                        new() { Id = "b_hud", Label = "HUD", Icon = EditorIcons.Gauge, Depth = 2 },
                        new() { Id = "b_menu", Label = "Pause Menu", Icon = EditorIcons.Bars, Depth = 2, Disabled = true },
                };

                Origami.Tree(paper, "op_tree_basic", 400, 220)
                    .Nodes(basicNodes)
                    .IsSelected(n => n.Id == _treeSelectedId)
                    .OnSelect(e => _treeSelectedId = e.Node.Id)
                    .OnDoubleClick(e => Toasts.Info("Tree", $"Double-clicked: {e.Node.Label}"))
                    .EmptyMessage("No items")
                    .Show();

                StateLine(paper, "op_tree_sel", $"Selected: {_treeSelectedId ?? "(none)"}");

                // ---- 2. Checkbox tree (package-style) ----
                StateLine(paper, "op_tree_h2", "Checkboxes - package import style");

                var checkNodes = new List<TreeNode>
                {
                    new() { Id = "c_assets", Label = "Assets", Icon = EditorIcons.Folder, IconColor = folderColor, HasChildren = true, Depth = 0, Checked = _treeChecked.Contains("c_assets") },
                      new() { Id = "c_tex", Label = "Textures", Icon = EditorIcons.Folder, IconColor = folderColor, HasChildren = true, Depth = 1, Checked = _treeChecked.Contains("c_tex") },
                        new() { Id = "c_grass", Label = "Grass.png", Icon = EditorIcons.FileImage, Depth = 2, Badge = "256 KB", Checked = _treeChecked.Contains("c_grass") },
                        new() { Id = "c_sky", Label = "Sky.hdr", Icon = EditorIcons.FileImage, Depth = 2, Badge = "1.8 MB", Checked = _treeChecked.Contains("c_sky") },
                        new() { Id = "c_dirt", Label = "Dirt.png", Icon = EditorIcons.FileImage, Depth = 2, Badge = "128 KB", Checked = _treeChecked.Contains("c_dirt") },
                      new() { Id = "c_models", Label = "Models", Icon = EditorIcons.Folder, IconColor = folderColor, HasChildren = true, Depth = 1, Checked = _treeChecked.Contains("c_models") },
                        new() { Id = "c_char", Label = "Character.glb", Icon = EditorIcons.VectorSquare, Depth = 2, Badge = "1.2 MB", Checked = _treeChecked.Contains("c_char") },
                        new() { Id = "c_prop", Label = "Barrel.obj", Icon = EditorIcons.VectorSquare, Depth = 2, Badge = "45 KB", Checked = _treeChecked.Contains("c_prop") },
                      new() { Id = "c_scripts", Label = "Scripts", Icon = EditorIcons.Folder, IconColor = folderColor, HasChildren = true, Depth = 1, Checked = _treeChecked.Contains("c_scripts") },
                        new() { Id = "c_main", Label = "GameManager.cs", Icon = EditorIcons.FileCode, Depth = 2, Checked = _treeChecked.Contains("c_main") },
                        new() { Id = "c_input", Label = "InputHandler.cs", Icon = EditorIcons.FileCode, Depth = 2, Checked = _treeChecked.Contains("c_input") },
                      new() { Id = "c_audio", Label = "Audio", Icon = EditorIcons.Folder, IconColor = folderColor, HasChildren = true, Depth = 1, Checked = _treeChecked.Contains("c_audio") },
                        new() { Id = "c_bgm", Label = "Background.ogg", Icon = EditorIcons.FileAudio, Depth = 2, Badge = "3.4 MB", Checked = _treeChecked.Contains("c_bgm") },
                };

                Origami.Tree(paper, "op_tree_check", 400, 220)
                    .Nodes(checkNodes)
                    .Checkboxes()
                    .IsSelected(n => n.Id == _treeSelectedId)
                    .OnSelect(e => _treeSelectedId = e.Node.Id)
                    .OnCheckedChanged((n, v) =>
                    {
                        if (v) _treeChecked.Add(n.Id);
                        else _treeChecked.Remove(n.Id);
                    })
                    .EmptyMessage("No items")
                    .Show();

                StateLine(paper, "op_tree_chk", $"Checked: {(_treeChecked.Count > 0 ? string.Join(", ", _treeChecked) : "(none)")}");

                // ---- 3. Trailing icons + colored labels ----
                StateLine(paper, "op_tree_h3", "Trailing icons, colored labels, status badges");

                var statusGreen = Color.FromArgb(255, 80, 200, 80);
                var statusYellow = Color.FromArgb(255, 220, 180, 40);
                var statusGrey = Color.FromArgb(255, 130, 130, 130);

                var fancyNodes = new List<TreeNode>
                {
                    new() { Id = "f_root", Label = "Package Contents", Icon = EditorIcons.BoxArchive, HasChildren = true, Depth = 0 },
                      new() { Id = "f_add1", Label = "NewTexture.png", Icon = EditorIcons.FileImage, Depth = 1, Badge = "Add", BadgeColor = statusGreen, LabelColor = statusGreen, TrailingIcon = EditorIcons.CirclePlus, TrailingIconColor = statusGreen },
                      new() { Id = "f_add2", Label = "NewShader.glsl", Icon = EditorIcons.WandMagicSparkles, Depth = 1, Badge = "Add", BadgeColor = statusGreen, LabelColor = statusGreen, TrailingIcon = EditorIcons.CirclePlus, TrailingIconColor = statusGreen },
                      new() { Id = "f_rep1", Label = "Player.cs", Icon = EditorIcons.FileCode, Depth = 1, Badge = "Replace", BadgeColor = statusYellow, LabelColor = statusYellow, TrailingIcon = EditorIcons.ArrowsRotate, TrailingIconColor = statusYellow },
                      new() { Id = "f_rep2", Label = "Config.json", Icon = EditorIcons.FileLines, Depth = 1, Badge = "Replace", BadgeColor = statusYellow, LabelColor = statusYellow, TrailingIcon = EditorIcons.ArrowsRotate, TrailingIconColor = statusYellow },
                      new() { Id = "f_skip1", Label = "Utils.cs", Icon = EditorIcons.FileCode, Depth = 1, Badge = "Identical", BadgeColor = statusGrey, LabelColor = statusGrey },
                      new() { Id = "f_skip2", Label = "README.md", Icon = EditorIcons.FileLines, Depth = 1, Badge = "Identical", BadgeColor = statusGrey, LabelColor = statusGrey },
                };

                Origami.Tree(paper, "op_tree_fancy", 400, 180)
                    .Nodes(fancyNodes)
                    .OnSelect(e => _treeSelectedId = e.Node.Id)
                    .IsSelected(n => n.Id == _treeSelectedId)
                    .OnTrailingIconClick(n => Toasts.Info("Tree", $"Action: {n.Label}"))
                    .Show();

                // ---- 4. Empty state ----
                StateLine(paper, "op_tree_h4", "Empty state");

                Origami.Tree(paper, "op_tree_empty", 400, 60)
                    .Nodes(new List<TreeNode>())
                    .EmptyMessage("No assets in this package")
                    .Show();
            }
        });
    }

    private void Section_State(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_state", "Live state").Body(() =>
        {
            var font = EditorTheme.DefaultFont;
            using (paper.Column("op_st_col").Height(UnitValue.Auto).RowBetween(2).Enter())
            {
                StateLine(paper, "st_basic",  $"Basic fruit: {_basicFruit}");
                StateLine(paper, "st_idx",    $"Int index: {_intIndex}");
                StateLine(paper, "st_enum",   $"Color enum: {_enum}");
                StateLine(paper, "st_country",$"Country: {_country.Code} > {_country.Name}");
                StateLine(paper, "st_search", $"Search picked: {_search}");
                StateLine(paper, "st_paged",  $"Paged value: {_paged}");
                StateLine(paper, "st_multi",  $"Multi fruits: {string.Join(", ", _multiBasic)}");
                StateLine(paper, "st_flags",  $"Flags: {_flags}");
                StateLine(paper, "st_flagsA", $"Flags (all): {_flagsAll}");
                StateLine(paper, "st_pw",     $"Password: {new string('*', _txtPassword.Length)} ({_txtPassword.Length} chars)");
                StateLine(paper, "st_ac",     $"Country picker: {_txtAutoComplete}");
                StateLine(paper, "st_int",    $"IntFilter: {_txtIntFilter}");
                StateLine(paper, "st_float",  $"FloatFilter: {_txtFloatFilter}");
                StateLine(paper, "st_nFloat", $"float: {_numFloat}");
                StateLine(paper, "st_nInt",   $"int: {_numInt}");
                StateLine(paper, "st_nByte",  $"byte: {_numByte}");
                StateLine(paper, "st_nClamp", $"clamped float: {_numClamped}");
                StateLine(paper, "st_nStep",  $"step float: {_numStep}");
                StateLine(paper, "st_swBasic", $"Switch basic: {_swBasic}");
                StateLine(paper, "st_cbBasic", $"Checkbox basic: {_cbBasic}");
                StateLine(paper, "st_cbAbc",   $"Checkbox A/B/C: {_cbA}/{_cbB}/{_cbC}");
                StateLine(paper, "st_rgShip",  $"Ship mode: {_rgShip}");
                StateLine(paper, "st_rgTheme", $"Theme: {_rgTheme}");
                StateLine(paper, "st_slBasic", $"Slider basic: {_slBasic:F3}");
                StateLine(paper, "st_slLog",   $"Slider log: {_slLog:F1}");
                StateLine(paper, "st_slBp",    $"Slider bipolar: {_slBipolar:F2}");
                StateLine(paper, "st_slDrag",  $"Drag start/end count: {_slDragCount} / {_slDragEndCount}");
                StateLine(paper, "st_rs",      $"RangeSlider float: [{_rsLow:F2}, {_rsHigh:F2}]");
                StateLine(paper, "st_rsInt",   $"RangeSlider int: [{_rsIntLow}, {_rsIntHigh}]");
                StateLine(paper, "st_btn_clk", $"Button click count: {_btnClickCount}");
                StateLine(paper, "st_btn_rc",  $"Button right-click: {_btnRightClickCount}");
                StateLine(paper, "st_btn_dc",  $"Button double-click: {_btnDoubleClickCount}");
                StateLine(paper, "st_bg_view", $"ButtonGroup view: {s_bgViewLabels[_bgViewMode]} (idx {_bgViewMode})");
                StateLine(paper, "st_bg_align",$"ButtonGroup align: {s_bgAlignLabels[_bgAlign]} (idx {_bgAlign})");
            }
        });
    }

    // -- Vector Fields ------------------------------------------

    private void Section_VectorFields(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_vec", "Vector Fields").Body(() =>
        {
            using (paper.Column("op_vf_col").Height(UnitValue.Auto).ColBetween(6).Enter())
            {
                LabelRow(paper, "vf_f2", "Float2", () =>
                    Origami.Float2Field(paper, "op_vf_f2", _vfFloat2, v => _vfFloat2 = v).Show());
                LabelRow(paper, "vf_f3", "Float3", () =>
                    Origami.Float3Field(paper, "op_vf_f3", _vfFloat3, v => _vfFloat3 = v).Show());
                LabelRow(paper, "vf_f4", "Float4", () =>
                    Origami.Float4Field(paper, "op_vf_f4", _vfFloat4, v => _vfFloat4 = v).Show());
                LabelRow(paper, "vf_i2", "Int2", () =>
                    Origami.Int2Field(paper, "op_vf_i2", _vfInt2, v => _vfInt2 = v).Show());
                LabelRow(paper, "vf_d3", "Double3", () =>
                    Origami.Double3Field(paper, "op_vf_d3", _vfDouble3, v => _vfDouble3 = v).Show());
            }
        });
    }

    // -- Color Fields -------------------------------------------

    private void Section_ColorFields(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_color", "Color Fields").Body(() =>
        {
            using (paper.Column("op_cf_col").Height(UnitValue.Auto).ColBetween(6).Enter())
            {
                LabelRow(paper, "cf_basic", "Basic", () =>
                    Origami.ColorField(paper, "op_cf_basic", _cfBasic, v => _cfBasic = v).Show());

                LabelRow(paper, "cf_noalpha", "No Alpha", () =>
                    Origami.ColorField(paper, "op_cf_noalpha", _cfNoAlpha, v => _cfNoAlpha = v).Alpha(false).Show());

                LabelRow(paper, "cf_readonly", "Read-Only", () =>
                    Origami.ColorField(paper, "op_cf_readonly", _cfReadOnly, _ => { }).ReadOnly().Show());

                LabelRow(paper, "cf_nopal", "No Palette", () =>
                    Origami.ColorField(paper, "op_cf_nopal", _cfNoPalette, v => _cfNoPalette = v).Palette(null).Show());
            }
        });
    }

    // -- Headers & Separators ---------------------------------

    private void Section_Headers(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_headers", "Headers & Separators").Body(() =>
        {
            using (paper.Column("op_hdr_col").Height(UnitValue.Auto).ColBetween(4).Enter())
            {
                // Styles
                Origami.Header(paper, "op_hdr_text", "Text Style (default)").Show();
                Origami.Header(paper, "op_hdr_line", "Line Style").Line().Show();
                Origami.Header(paper, "op_hdr_linec", "Centered Line").LineCentered().Show();
                Origami.Header(paper, "op_hdr_box", "Box Style").Box().Show();
                Origami.Header(paper, "op_hdr_ul", "Underline Style").Underline().Show();
                Origami.Separator(paper, "op_hdr_sep");

                // With icons
                Origami.Header(paper, "op_hdr_ico_t", "With Icon").Icon(EditorIcons.Gear).Show();
                Origami.Header(paper, "op_hdr_ico_l", "Icon + Line").Icon(EditorIcons.Cube).Line().Show();
                Origami.Header(paper, "op_hdr_ico_b", "Icon + Box").Icon(EditorIcons.Star).Box().Show();

                // With badges
                Origami.Header(paper, "op_hdr_bdg", "Section Title").Badge("3 items").Line().Show();
                Origami.Header(paper, "op_hdr_bdg2", "Settings").Badge("Advanced").Icon(EditorIcons.Gear).Box().Show();

                // Variants
                Origami.Header(paper, "op_hdr_v_pri", "Primary").Primary().Box().Show();
                Origami.Header(paper, "op_hdr_v_suc", "Success").Success().Line().Show();
                Origami.Header(paper, "op_hdr_v_wrn", "Warning").Warning().Underline().Show();
                Origami.Header(paper, "op_hdr_v_dng", "Danger").Danger().Box().Show();
                Origami.Header(paper, "op_hdr_v_inf", "Info").Info().LineCentered().Show();

                // Thick separators
                Origami.Separator(paper, "op_hdr_sep2");
                Origami.Header(paper, "op_hdr_thick", "Thick Line").Line().Thickness(4).Show();
                Origami.Header(paper, "op_hdr_thicc", "Thick Line B").Underline().Thickness(4).Primary().Show();
            }
        });
    }

    private void Section_PropertyGrid(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_pg", "PropertyGrid").DefaultExpanded().Body(() =>
        {
            Origami.Header(paper, "pg_h1", "Full Object (all attribute types)").Underline().Show();

            StateLine(paper, "pg_info",
                "Reflects serializable fields, respects [Header], [Space], [Range], [ShowIf], [ReadOnly], [TextArea], [Button], enums, lists, nested objects.");

            using (paper.Column("pg_col1").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(8, 8, 8, 8)
                .BackgroundColor(System.Drawing.Color.FromArgb(20, 255, 255, 255))
                .Rounded(4).Enter())
            {
                Origami.PropertyGrid(paper, "pg_demo1", _pgTarget, EditorApplication.PropertyGridConfig).Show();
            }

            Origami.Header(paper, "pg_h2", "Second Instance (different defaults)").Underline().Show();

            StateLine(paper, "pg_info2",
                "Same class, different initial values. HasMagic is true so conditional fields are visible.");

            using (paper.Column("pg_col2").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(8, 8, 8, 8)
                .BackgroundColor(System.Drawing.Color.FromArgb(20, 255, 255, 255))
                .Rounded(4).Enter())
            {
                Origami.PropertyGrid(paper, "pg_demo2", _pgTarget2, EditorApplication.PropertyGridConfig).Show();
            }
        });
    }

    private void Section_Labels(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_labels", "Labels").Body(() =>
        {
            using (paper.Column("op_lbl_col").Height(UnitValue.Auto).ColBetween(6).Enter())
            {
                // Sizes (XS / SM / MD / LG / XL)
                LabelRow(paper, "lbl_sizes", "Size presets", () =>
                {
                    using (paper.Row("op_lbl_sz_row").Height(36).RowBetween(10).Enter())
                    {
                        Origami.Label(paper, "op_lbl_xs", "XS").XS().Show();
                        Origami.Label(paper, "op_lbl_sm", "SM").SM().Show();
                        Origami.Label(paper, "op_lbl_md", "MD").MD().Show();
                        Origami.Label(paper, "op_lbl_lg", "LG").LG().Show();
                        Origami.Label(paper, "op_lbl_xl", "XL").XL().Show();
                    }
                });

                // Variants
                LabelRow(paper, "lbl_variants", "Variants", () =>
                {
                    using (paper.Row("op_lbl_var_row").Height(EditorTheme.RowHeight).RowBetween(10).Enter())
                    {
                        Origami.Label(paper, "op_lbl_v_def", "Default").Show();
                        Origami.Label(paper, "op_lbl_v_pri", "Primary").Primary().Show();
                        Origami.Label(paper, "op_lbl_v_suc", "Success").Success().Show();
                        Origami.Label(paper, "op_lbl_v_war", "Warning").Warning().Show();
                        Origami.Label(paper, "op_lbl_v_dan", "Danger").Danger().Show();
                        Origami.Label(paper, "op_lbl_v_inf", "Info").Info().Show();
                        Origami.Label(paper, "op_lbl_v_sub", "Subtle").Subtle().Show();
                    }
                });

                // Pills / badges
                LabelRow(paper, "lbl_pills", "Pills (chip style)", () =>
                {
                    using (paper.Row("op_lbl_pill_row").Height(24).RowBetween(8).Enter())
                    {
                        Origami.Label(paper, "op_lbl_pill_s", "Ready").Success().Pill().Padding(8, 0).Show();
                        Origami.Label(paper, "op_lbl_pill_w", "Warning").Warning().Pill().Padding(8, 0).Show();
                        Origami.Label(paper, "op_lbl_pill_d", "Failed").Danger().Pill().Padding(8, 0).Show();
                        Origami.Label(paper, "op_lbl_pill_i", "Info").Info().Pill().Padding(8, 0).Show();
                    }
                });

                // Border / background combos
                LabelRow(paper, "lbl_boxes", "Background + Border", () =>
                {
                    using (paper.Row("op_lbl_box_row").Height(24).RowBetween(8).Enter())
                    {
                        Origami.Label(paper, "op_lbl_box_a", "Bordered").Border().Padding(8, 0).Show();
                        Origami.Label(paper, "op_lbl_box_b", "Filled").Primary().Background().Padding(8, 0).Show();
                        Origami.Label(paper, "op_lbl_box_c", "Outlined").Danger().Border().Padding(8, 0).Show();
                        Origami.Label(paper, "op_lbl_box_d", "Both").Success().Background().Border().Padding(8, 0).Show();
                    }
                });

                // Decorations (underline / strikethrough / double underline)
                LabelRow(paper, "lbl_decor", "Underline / Strike", () =>
                {
                    using (paper.Row("op_lbl_decor_row").Height(EditorTheme.RowHeight).RowBetween(10).Enter())
                    {
                        Origami.Label(paper, "op_lbl_un", "Underline").Underline().Show();
                        Origami.Label(paper, "op_lbl_dun", "Double").DoubleUnderline().Show();
                        Origami.Label(paper, "op_lbl_st", "Strikethrough").Strikethrough().Show();
                    }
                });

                // Shadow + inset effects
                LabelRow(paper, "lbl_fx", "Shadow / Inset", () =>
                {
                    using (paper.Row("op_lbl_fx_row").Height(36).RowBetween(14).Enter())
                    {
                        Origami.Label(paper, "op_lbl_shadow", "Shadowed")
                            .LG().Shadow(dx: 1, dy: 1).Show();
                        Origami.Label(paper, "op_lbl_shadow2", "Coloured Shadow")
                            .LG().Primary().Shadow(Color.FromArgb(180, 90, 60, 180), dx: 0, dy: 2).Show();
                        Origami.Label(paper, "op_lbl_inset", "Engraved")
                            .LG().Subtle().Inset().Show();
                        Origami.Label(paper, "op_lbl_combo", "ALL FX")
                            .LG().Danger().Shadow(dx: 2, dy: 2).Underline().Show();
                    }
                });

                // Icons (leading + trailing)
                LabelRow(paper, "lbl_icons", "Icons", () =>
                {
                    using (paper.Row("op_lbl_ic_row").Height(EditorTheme.RowHeight).RowBetween(10).Enter())
                    {
                        Origami.Label(paper, "op_lbl_ic_l", "Saved")
                            .Success().LeadingIcon(EditorIcons.Check).Show();
                        Origami.Label(paper, "op_lbl_ic_t", "Open")
                            .LeadingIcon(EditorIcons.FolderOpen).TrailingIcon(EditorIcons.AngleRight).Show();
                        Origami.Label(paper, "op_lbl_ic_w", "Warning")
                            .Warning().LeadingIcon(EditorIcons.TriangleExclamation).Show();
                    }
                });

                // Click + tooltip + disabled
                LabelRow(paper, "lbl_interact", "Click + Tooltip + Disabled", () =>
                {
                    using (paper.Row("op_lbl_link_row").Height(EditorTheme.RowHeight).RowBetween(14).Enter())
                    {
                        Origami.Label(paper, "op_lbl_link", $"Click me ({_labelClickCount})")
                            .Primary().Underline()
                            .Tooltip("This label is clickable")
                            .OnClick(() => _labelClickCount++)
                            .Show();

                        Origami.Label(paper, "op_lbl_disabled", "Disabled")
                            .Disabled().Show();
                    }
                });

                // Truncation (fixed width, long text)
                LabelRow(paper, "lbl_trunc", "Ellipsis truncation", () =>
                {
                    using (paper.Row("op_lbl_trunc_row").Height(EditorTheme.RowHeight).RowBetween(8).Enter())
                    {
                        Origami.Label(paper, "op_lbl_trunc_1",
                            "This text is too long to fit and will be ellipsized")
                            .Width(220).Ellipsis().Border().Padding(6, 0).Show();

                        Origami.Label(paper, "op_lbl_trunc_2",
                            "Centered overflowing label inside a fixed width box")
                            .Width(260).Ellipsis().AlignCenter().Border().Padding(6, 0).Show();
                    }
                });
            }
        });
    }

    private void Section_Loading(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_loading", "Loading (Progress, Spinner, Skeleton)").Body(() =>
        {
            using (paper.Column("op_load_col").Height(UnitValue.Auto).ColBetween(8).Enter())
            {
                // Progress sizes (determinate)
                LabelRow(paper, "load_pb_sizes", "Sizes", () =>
                {
                    using (paper.Column("op_pb_sz_col").Height(UnitValue.Auto).ColBetween(4).Enter())
                    {
                        Origami.ProgressBar(paper, "op_pb_xs", _loadProgress).XS().ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_sm", _loadProgress).SM().ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_md", _loadProgress).MD().ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_lg", _loadProgress).LG().ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_xl", _loadProgress).XL().ShowPercent().Show();
                    }
                });

                // Variants (all at LG so colour reads)
                LabelRow(paper, "load_pb_variants", "Variants", () =>
                {
                    using (paper.Column("op_pb_var_col").Height(UnitValue.Auto).ColBetween(4).Enter())
                    {
                        Origami.ProgressBar(paper, "op_pb_v_pri", _loadProgress).LG().Primary().Label("Primary").ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_v_suc", _loadProgress).LG().Success().Label("Success").ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_v_war", _loadProgress).LG().Warning().Label("Warning").ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_v_dan", _loadProgress).LG().Danger().Label("Danger").ShowPercent().Show();
                        Origami.ProgressBar(paper, "op_pb_v_inf", _loadProgress).LG().Info().Label("Info").ShowPercent().Show();
                    }
                });

                // Striped + glow + square
                LabelRow(paper, "load_pb_decor", "Striped / Glow / Square", () =>
                {
                    using (paper.Column("op_pb_dec_col").Height(UnitValue.Auto).ColBetween(4).Enter())
                    {
                        Origami.ProgressBar(paper, "op_pb_square", _loadProgress).LG().Info().Square().Label("Square").ShowPercent().Show();
                    }
                });

                // Indeterminate
                LabelRow(paper, "load_pb_indet", "Indeterminate", () =>
                {
                    using (paper.Column("op_pb_indet_col").Height(UnitValue.Auto).ColBetween(4).Enter())
                    {
                        Origami.ProgressBar(paper, "op_pb_indet_1", 0f).LG().Primary().Indeterminate().Label("Loading...").Show();
                    }
                });

                // Driver slider for the determinate samples above
                LabelRow(paper, "load_pb_drive", "Drive determinate value", () =>
                    Origami.Slider(paper, "op_pb_drive_v", _loadProgress, v => _loadProgress = v, 0f, 1f).Format("F2").Show());

                // Spinners - styles + sizes
                LabelRow(paper, "load_spinner_styles", "Spinner styles", () =>
                {
                    using (paper.Row("op_sp_st_row").Height(40).RowBetween(20).Enter())
                    {
                        Origami.Spinner(paper, "op_sp_arc").Arc().LG().Label("Arc").Show();
                        Origami.Spinner(paper, "op_sp_dual").DualArc().LG().Success().Label("DualArc").Show();
                        Origami.Spinner(paper, "op_sp_dots").Dots().LG().Warning().Label("Dots").Show();
                        Origami.Spinner(paper, "op_sp_pulse").Pulse().LG().Info().Label("Pulse").Show();
                    }
                });

                LabelRow(paper, "load_spinner_sizes", "Spinner sizes", () =>
                {
                    using (paper.Row("op_sp_sz_row").Height(48).RowBetween(16).Enter())
                    {
                        Origami.Spinner(paper, "op_sp_xs").XS().Show();
                        Origami.Spinner(paper, "op_sp_sm").SM().Show();
                        Origami.Spinner(paper, "op_sp_md").MD().Show();
                        Origami.Spinner(paper, "op_sp_lg").LG().Show();
                        Origami.Spinner(paper, "op_sp_xl").XL().Show();
                    }
                });

                // Skeletons
                LabelRow(paper, "load_skeleton_text", "Skeleton: text lines", () =>
                {
                    using (paper.Column("op_sk_text_col").Height(UnitValue.Auto).ColBetween(6).Enter())
                    {
                        Origami.Skeleton(paper, "op_sk_l1").TextLine(280).Show();
                        Origami.Skeleton(paper, "op_sk_l2").TextLine(220).Show();
                        Origami.Skeleton(paper, "op_sk_l3").TextLine(160).Show();
                    }
                });

                LabelRow(paper, "load_skeleton_shapes", "Skeleton: shapes", () =>
                {
                    using (paper.Row("op_sk_sh_row").Height(60).RowBetween(12).Enter())
                    {
                        Origami.Skeleton(paper, "op_sk_av").Avatar(48).Show();
                        Origami.Skeleton(paper, "op_sk_pill").Pill().Size(110, 24).Show();
                        Origami.Skeleton(paper, "op_sk_rect").Rect().Size(160, 50).Rounding(6).Show();
                    }
                });

                LabelRow(paper, "load_skeleton_card", "Skeleton: card layout", () =>
                {
                    using (paper.Row("op_sk_card_row").Height(80).RowBetween(12).Enter())
                    {
                        Origami.Skeleton(paper, "op_sk_card_av").Avatar(64).Show();
                        using (paper.Column("op_sk_card_col").Height(UnitValue.Auto).ColBetween(8).Enter())
                        {
                            Origami.Skeleton(paper, "op_sk_card_t1").TextLine(220).Show();
                            Origami.Skeleton(paper, "op_sk_card_t2").TextLine(160).Show();
                            Origami.Skeleton(paper, "op_sk_card_t3").TextLine(180).Show();
                        }
                    }
                });
            }
        });
    }

    // -- App Bar --------------------------------------------------

    private void Section_AppBar(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_appbar", "App Bar (MenuBar / Footer)").Body(() =>
        {
            using (paper.Column("op_ab_col").Height(UnitValue.Auto).ColBetween(8).Enter())
            {
                Origami.Label(paper, "op_ab_info", "The editor's menubar and statusbar both use Origami.AppBar. Below is an inline demo:").Show();

                // Inline demo menubar (not self-directed, just renders in flow)
                var demoFileItems = new System.Collections.Generic.List<AppMenuItem>
                {
                    new("New", () => { }),
                    new("Open", () => { }),
                    new("Save", () => { }),
                    AppMenuItem.Separator(),
                    new("Exit", () => { }),
                };
                var demoEditItems = new System.Collections.Generic.List<AppMenuItem>
                {
                    new("Undo", () => { }),
                    new("Redo", () => { }),
                    AppMenuItem.Separator(),
                    new("Cut", () => { }),
                    new("Copy", () => { }),
                    new("Paste", () => { }),
                };
                var demoViewItems = new System.Collections.Generic.List<AppMenuItem>
                {
                    new("Zoom In", () => { }),
                    new("Zoom Out", () => { }),
                    AppMenuItem.Separator(),
                    new("Show Grid", () => _ctxToggleA = !_ctxToggleA) { IsCheckedFunc = () => _ctxToggleA },
                };

                // Note: This demo doesn't use SelfDirected positioning,
                // so it renders inline. The real menubar uses .Position(0,0).
                Origami.AppBar(paper, "op_ab_demo")
                    .Height(28)
                    .Menu("File", demoFileItems)
                    .Menu("Edit", demoEditItems)
                    .Menu("View", demoViewItems)
                    .Center(p =>
                    {
                        var f = Origami.Current.Font;
                        if (f != null)
                            p.Box("op_ab_center").Height(28).Width(UnitValue.Auto)
                                .Text("Center Content", f).TextColor(Origami.Current.Ink.C400)
                                .FontSize(Origami.Current.Metrics.FontSize - 2)
                                .Alignment(TextAlignment.MiddleCenter);
                    })
                    .Right(p =>
                    {
                        var f = Origami.Current.Font;
                        if (f != null)
                            p.Box("op_ab_right").Height(28).Width(UnitValue.Auto).ChildRight(8)
                                .Text("v1.0.0", f).TextColor(Origami.Current.Ink.C400)
                                .FontSize(Origami.Current.Metrics.FontSize - 2)
                                .Alignment(TextAlignment.MiddleRight);
                    })
                    .Show();
            }
        });
    }

    // -- Context Menus --------------------------------------------

    private bool _ctxToggleA = true;
    private bool _ctxToggleB;

    private void Section_ContextMenus(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_ctx", "Context Menus").Body(() =>
        {
            using (paper.Column("op_ctx_col").Height(UnitValue.Auto).ColBetween(6).Enter())
            {
                // Basic right-click area
                using (paper.Box("op_ctx_basic")
                    .Height(60)
                    .BackgroundColor(Origami.Current.Neutral.C200)
                    .Rounded(4)
                    .Text("Right-click me for a basic menu", Origami.Current.Font)
                    .TextColor(Origami.Current.Ink.C400)
                    .FontSize(Origami.Current.Metrics.FontSize)
                    .Alignment(TextAlignment.MiddleCenter)
                    .Enter())
                {
                    Origami.RightClickMenu(paper, "op_ctx_basic_m", b =>
                    {
                        b.Item("Cut", () => { }, icon: EditorIcons.Scissors);
                        b.Item("Copy", () => { }, icon: EditorIcons.Copy);
                        b.Item("Paste", () => { }, icon: EditorIcons.Paste);
                        b.Separator();
                        b.Item("Delete", () => { }, icon: EditorIcons.Trash);
                    });
                }

                // With toggles and submenus
                using (paper.Box("op_ctx_adv")
                    .Height(60)
                    .BackgroundColor(Origami.Current.Neutral.C200)
                    .Rounded(4)
                    .Text("Right-click for toggles + submenu", Origami.Current.Font)
                    .TextColor(Origami.Current.Ink.C400)
                    .FontSize(Origami.Current.Metrics.FontSize)
                    .Alignment(TextAlignment.MiddleCenter)
                    .Enter())
                {
                    Origami.RightClickMenu(paper, "op_ctx_adv_m", b =>
                    {
                        b.Toggle("Show Grid", () => _ctxToggleA = !_ctxToggleA, () => _ctxToggleA);
                        b.Toggle("Wireframe", () => _ctxToggleB = !_ctxToggleB, () => _ctxToggleB);
                        b.Separator();
                        b.Submenu("Transform", sub =>
                        {
                            sub.Item("Reset Position", () => { });
                            sub.Item("Reset Rotation", () => { });
                            sub.Item("Reset Scale", () => { });
                        });
                        b.Submenu("Create", sub =>
                        {
                            sub.Item("Empty Object", () => { }, icon: EditorIcons.Cube);
                            sub.Item("Cube", () => { }, icon: EditorIcons.Cube);
                            sub.Item("Sphere", () => { }, icon: EditorIcons.Circle);
                        });
                        b.Separator();
                        b.Item("Disabled Item", () => { }, enabled: false);
                    });
                }

                // Programmatic open
                LabelRow(paper, "ctx_prog", "Programmatic Open", () =>
                    Origami.Button(paper, "op_ctx_prog", "Open Menu Here", () =>
                        Origami.ContextMenu(400, 300, b =>
                        {
                            b.Item("Action A", () => { });
                            b.Item("Action B", () => { });
                        })).Show());

                StateLine(paper, "ctx_state", $"Context menu open: {ContextMenu.IsOpen}");
            }
        });
    }

    // -- Modals --------------------------------------------------

    private void Section_Modals(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_modals", "Modals").Body(() =>
        {
            using (paper.Column("op_modal_col").Height(UnitValue.Auto).ColBetween(6).Enter())
            {
                LabelRow(paper, "mod_confirm", "Confirm Dialog", () =>
                    Origami.Button(paper, "op_mod_confirm", "Show Confirm", () =>
                        Origami.Confirm("Delete Item", "Are you sure you want to delete this item? This cannot be undone.",
                            () => { /* yes */ }, () => { /* no */ })).Show());

                LabelRow(paper, "mod_message", "Message Dialog", () =>
                    Origami.Button(paper, "op_mod_message", "Show Message", () =>
                        Origami.Message("Hello", "This is a simple informational message.")).Show());

                LabelRow(paper, "mod_custom", "Custom Content", () =>
                    Origami.Button(paper, "op_mod_custom", "Show Custom", () =>
                    {
                        var entry = Origami.Dialog("Settings", p =>
                        {
                            Origami.Header(p, "mod_c_hdr", "Configuration").Underline().Show();
                            Origami.Label(p, "mod_c_info", "Modify settings below then click Apply.").Show();
                            Origami.Checkbox(p, "mod_c_chk", false, _ => { }).LabelRight("Enable feature X").Show();
                            Origami.Slider(p, "mod_c_sl", 0.5f, _ => { }, 0f, 1f).Show();
                        }, 450);
                        entry.Button("Apply", () => Modal.Pop(), OrigamiVariant.Success);
                        entry.Button("Cancel", () => Modal.Pop());
                    }).Show());

                LabelRow(paper, "mod_stack", "Stacked Modals", () =>
                    Origami.Button(paper, "op_mod_stack", "Push 1st", () =>
                    {
                        var first = new DialogModal { Title = "First Modal", Width = 350, CloseOnEscape = true };
                        first.DrawContent = p =>
                        {
                            Origami.Label(p, "mod_s1_msg", "This is the first modal. Push another on top!").Show();
                            Origami.Button(p, "mod_s1_push", "Push 2nd Modal", () =>
                            {
                                var second = new DialogModal { Title = "Second Modal", Width = 320, CloseOnEscape = true };
                                second.DrawContent = p2 =>
                                {
                                    Origami.Label(p2, "mod_s2_msg", "Stacked on top! Press Escape or click Close.").Show();
                                };
                                second.Button("Close", () => Modal.Pop(), OrigamiVariant.Primary);
                                Modal.Push(second);
                            }).Primary().Show();
                        };
                        first.Button("Close", () => Modal.Pop(), OrigamiVariant.Primary);
                        Modal.Push(first);
                    }).Show());

                LabelRow(paper, "mod_danger", "Danger Confirm", () =>
                    Origami.Button(paper, "op_mod_danger", "Delete All", () =>
                    {
                        var entry = new DialogModal { Title = "Destructive Action", Width = 380, CloseOnEscape = true };
                        entry.DrawContent = p =>
                            Origami.Label(p, "mod_d_msg", "This will permanently delete everything. Are you absolutely sure?").Show();
                        entry.Button("Delete Everything", () => Modal.Pop(), OrigamiVariant.Danger);
                        entry.Button("Cancel", () => Modal.Pop());
                        Modal.Push(entry);
                    }).Danger().Show());

                LabelRow(paper, "mod_backdrop", "Close on Backdrop", () =>
                    Origami.Button(paper, "op_mod_backdrop", "Show", () =>
                    {
                        var entry = new DialogModal { Title = "Click Outside to Close", Width = 350, CloseOnBackdrop = true };
                        entry.DrawContent = p =>
                            Origami.Label(p, "mod_b_msg", "Click the darkened area outside this dialog to dismiss it.").Show();
                        entry.Button("Or Click Here", () => Modal.Pop(), OrigamiVariant.Primary);
                        Modal.Push(entry);
                    }).Show());

                StateLine(paper, "mod_count", $"Modal stack depth: {Modal.Count}");
            }
        });
    }

    // -- Tiny layout helpers ------------------------------------

    private static void LabelRow(Paper paper, string id, string label, Action drawControl)
    {
        var font = EditorTheme.DefaultFont;
        using (paper.Row($"op_lr_{id}").Height(UnitValue.Auto).RowBetween(8).Enter())
        {
            paper.Box($"op_lr_{id}_lbl")
                .Width(280).Height(28)
                .Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable()
                .Text(label, font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 1);

            using (paper.Box($"op_lr_{id}_ctl").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
            {
                drawControl();
            }
        }
    }

    private static void StateLine(Paper paper, string id, string text)
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).Height(18)
            .Alignment(TextAlignment.MiddleLeft).IsNotInteractable()
            .Text(text, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 2);
    }

    private static Color FruitColor(string fruit) => fruit switch
    {
        "Apple"      => Color.FromArgb(220, 70, 70),
        "Banana"     => Color.FromArgb(245, 210, 60),
        "Cherry"     => Color.FromArgb(180, 30, 60),
        "Date"       => Color.FromArgb(120, 80, 50),
        "Elderberry" => Color.FromArgb(80, 40, 90),
        "Fig"        => Color.FromArgb(140, 90, 130),
        "Grape"      => Color.FromArgb(110, 60, 160),
        "Honeydew"   => Color.FromArgb(170, 220, 130),
        _            => Color.Gray,
    };
}
