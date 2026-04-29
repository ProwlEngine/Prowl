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
            }
        });
    }

    // ── Tiny layout helpers ────────────────────────────────────

    private static void LabelRow(Paper paper, string id, string label, Action drawControl)
    {
        var font = EditorTheme.DefaultFont;
        using (paper.Row($"op_lr_{id}").Height(28).RowBetween(8).Enter())
        {
            paper.Box($"op_lr_{id}_lbl")
                .Width(280).Height(28)
                .Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable()
                .Text(label, font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 1);

            using (paper.Box($"op_lr_{id}_ctl").Width(UnitValue.Stretch()).Height(28).Enter())
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
