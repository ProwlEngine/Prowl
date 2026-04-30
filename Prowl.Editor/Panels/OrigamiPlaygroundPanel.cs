// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.Panels;

/// <summary>
/// Stress-test surface for every Origami widget. Currently focused on
/// <see cref="DropdownBuilder{T}"/> and <see cref="MultiDropdownBuilder{T}"/> — every
/// feature exposed on those builders has at least one concrete demo so visual regressions
/// are easy to spot.
/// </summary>
[EditorWindow("Debug/Origami Playground")]
public class OrigamiPlaygroundPanel : DockPanel
{
    public override string Title => "Origami Playground";
    public override string Icon => EditorIcons.Flask;

    // ── Demo enums ──────────────────────────────────────────────

    public enum Color8 { Red, Orange, Yellow, Green, Blue, Indigo, Violet, Black }

    [Flags]
    public enum Compass
    {
        None = 0, North = 1 << 0, South = 1 << 1, East = 1 << 2, West = 1 << 3,
        Up = 1 << 4, Down = 1 << 5, Forward = 1 << 6, Back = 1 << 7,
    }

    private record Country(string Code, string Name, string Continent);

    // ── State ──────────────────────────────────────────────────

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

    // ── TextField state ────────────────────────────────────────
    private string _txtPlain = "";
    private string _txtSearch = "";
    private string _txtPassword = "hunter2";
    private string _txtMulti = "Type a few lines.\nWraps automatically when DoWrap is on.";
    private string _txtAutoComplete = "";
    private string _txtClearBtn = "Clear me with the X >";
    private string _txtReadOnly = "Read-only — content can't change.";
    private string _txtIntFilter = "";
    private string _txtFloatFilter = "";
    private string _txtAlphaNum = "";
    private string _txtNoSpaces = "";
    private string _txtError = "bad";
    private string _txtValidator = "abc";
    private string _txtHelper = "";
    private string _txtLeading = "";
    private string _txtTrailing = "";

    // ── Toggle state ──────────────────────────────────────────
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

    // ── NumericField state ────────────────────────────────────
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

    // ── Static datasets ────────────────────────────────────────

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

    // ── OnGUI ──────────────────────────────────────────────────

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Origami.ScrollView(paper, "op_scroll", width, height)
            .Padding(12, 12, 12, 12)
            .ColSpacing(8)
            .Body(() =>
            {
                EditorGUI.Header(paper, "op_h_root", "Origami Dropdown Playground");

                paper.Box("op_intro").Height(20)
                    .Alignment(TextAlignment.MiddleLeft).IsNotInteractable()
                    .Text("Every demo controls a real value. Open multiple — they're all independent.", font)
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
                Section_State(paper);
            });
    }

    // ── Sections ───────────────────────────────────────────────

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

    // ── TextField sections ─────────────────────────────────────

    private void Section_TextFieldBasics(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tf_basics", "TextField — basics").Body(() =>
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
        Origami.Foldout(paper, "op_fo_tf_filters", "TextField — filter presets").Body(() =>
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
                        .Placeholder("try pressing space — it's filtered")
                        .Show());
            }
        });
    }

    private void Section_TextFieldValidation(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_tf_valid", "TextField — validation / helper").Body(() =>
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
        Origami.Foldout(paper, "op_fo_tf_ac", "TextField — autocomplete").Body(() =>
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

    // ── NumericField sections ──────────────────────────────────

    private void Section_NumericTypes(Paper paper)
    {
        Origami.Foldout(paper, "op_fo_num_types", "NumericField — every numeric type").Body(() =>
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
        Origami.Foldout(paper, "op_fo_num_clamp", "NumericField — Min / Max / Step").Body(() =>
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
        Origami.Foldout(paper, "op_fo_num_culture", "NumericField — culture").Body(() =>
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

    // ── Toggle sections ────────────────────────────────────────

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
            }
        });
    }

    // ── Tiny layout helpers ────────────────────────────────────

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
