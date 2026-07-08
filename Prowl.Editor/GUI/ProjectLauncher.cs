using System;
using System.IO;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Rosetta;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI;

/// <summary>
/// Full-screen project launcher shown at startup before the editor loads.
/// Shows recent projects, and allows creating/opening projects.
/// </summary>
public static class ProjectLauncher
{
    public static bool IsOpen { get; private set; } = true;

    private static string _newProjectName = "Untitled";
    private static string _newProjectPath = "";
    private static string _search = "";
    private static int _tab;   // 0 = Recent, 1 = New Project
    private static float _animTime;
    private static NebulaBackground? _nebula;

    // Favorite-star icon in both Font Awesome weights: outline when unfavored, filled when favored.
    // EditorGlyphIcon resolves the face at draw time, so building these before fonts load is fine.
    private static readonly IOrigamiIcon _starOutline = new EditorGlyphIcon(EditorIcons.Star, weight: GlyphWeight.Outline);
    private static readonly IOrigamiIcon _starSolid = new EditorGlyphIcon(EditorIcons.Star, weight: GlyphWeight.Solid);

    private const float TS = 1.4f;

    private static UnitValue ST => UnitValue.StretchOne;

    private static Color Col(int r, int g, int b, float a = 1f) => Color.FromArgb((int)Math.Round(a * 255), r, g, b);
    private static Color WinGlass => Col(20, 16, 36, 0.72f);
    private static Color GlassIn => EditorTheme.Glass;
    private static Color Raised => Col(38, 32, 54, 0.8f);
    private static Color Bd => Color.FromArgb(33, EditorTheme.Accent.R, EditorTheme.Accent.G, EditorTheme.Accent.B);
    private static Color BdSoft => EditorTheme.BorderSoft;
    private static Color BdStrong => EditorTheme.BorderStrong;
    private static Color InputBd => Color.FromArgb(41, EditorTheme.Accent.R, EditorTheme.Accent.G, EditorTheme.Accent.B);
    private static Color CardBg => Col(255, 255, 255, 0.025f);
    private static Color Acc => EditorTheme.Accent;
    private static Color AccBright => EditorTheme.AccentBright;
    private static Color Acc300 => EditorTheme.AccentText;
    private static Color THi => EditorTheme.Ink500;
    private static Color TBody => EditorTheme.Ink400;
    private static Color TMid => EditorTheme.Ink300;
    private static Color TLo => EditorTheme.InkDim;

    // Cycled tip strip drawn at the bottom of the launcher background.
    private static readonly string[] _tipKeys =
    {
        "launcher.tip.orbit",
        "launcher.tip.dolly",
        "launcher.tip.pan",
        "launcher.tip.fly",
        "launcher.tip.fly_speed",
        "launcher.tip.fly_updown",
        "launcher.tip.focus",
        "launcher.tip.gizmos",
        "launcher.tip.snap",
        "launcher.tip.duplicate",
        "launcher.tip.rename",
        "launcher.tip.math",
        "launcher.tip.math_ops",
        "launcher.tip.drag_asset",
        "launcher.tip.orient_cube",
        "launcher.tip.undo",
        "launcher.tip.discord",
        "launcher.tip.assignref",
        "launcher.tip.pingref",
        "launcher.tip.pickref",
        "launcher.tip.go_to_component",
        "launcher.tip.create_menu",
        "launcher.tip.component_menu",
        "launcher.tip.escape_unlock",
    };

    private const float _tipDuration = 8f;
    private const float _tipFadeTime = 0.45f;
    private static int _tipIndex;
    private static float _tipTimer;

    public static void Initialize()
    {
        _newProjectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prowl Projects");
        IsOpen = true;
        _tab = 0;
        _animTime = 0;
        _tipIndex = Random.Shared.Next(_tipKeys.Length);
        _tipTimer = 0;
    }

    private static void AdvanceTip()
    {
        _tipIndex = (_tipIndex + 1) % _tipKeys.Length;
        _tipTimer = 0;
    }

    public static void Close()
    {
        IsOpen = false;
    }

    public static void Draw(Paper paper, float dt, bool forceDraw = false)
    {
        if (!IsOpen && !forceDraw) return;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        _animTime += dt;

        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        // Animated Nebula backdrop.
        _nebula ??= new NebulaBackground(paper);
        NebulaBackground.DrawEditorBackground(paper, _nebula, "pl_bg", w, h, dt);

        using (paper.Box("pl_container").PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h).Enter())
        using (paper.Column("pl_window")
            .Width(940).Height(620)
            .Margin(ST, ST, ST, ST)
            .BackgroundColor(WinGlass)
            .BackdropBlur(22)
            .BorderColor(Bd).BorderWidth(1)
            .Rounded(10)
            .DropShadow(0, 16, 44, -8, Col(0, 0, 0, 0.9f))
            .Clip()
            .Enter())
        {
            Header(paper, font);
            paper.Box("pl_head_div").Height(1).BackgroundColor(BdSoft);
            if (_tab == 0) RecentBody(paper, font);
            else NewProjectBody(paper, font);
        }
    }

    private static string EngineVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1";
        int plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        return v;
    }

    // ---- Header (brand + tab pills) ---------------------------------
    private static void Header(Paper P, Scribe.FontFile font)
    {
        var display = EditorTheme.FontDisplay ?? EditorTheme.DefaultBoldFont ?? font;

        using (P.Row("pl_header").Height(64).Padding(26, 26, 0, 0).Enter())
        {
            using (P.Box("pl_logo").Width(60).Height(60).Margin(0, 0, ST, ST).Enter())
                P.Draw((vg, r) => EditorIcons.ProwlLogo.Draw(vg, r, THi, 1f));

            // Wordmark scaled up to fill the header height (version line removed), vertically centered.
            P.Box("pl_prowl").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(13, 0, ST, ST)
                .Text("PROWL", EditorTheme.FontLogo ?? display).FontSize(30f * TS).LetterSpacing(6f).TextColor(THi).Alignment(TextAlignment.MiddleLeft);

            P.Box("pl_hspacer").Width(ST);

            using (P.Row("pl_lang_wrap").Width(UnitValue.Auto).Height(34).Margin(0, 10, ST, ST).Enter())
                LanguageDropdown(P, font);

            using (P.Row("pl_tabs").Width(UnitValue.Auto).Height(34).Margin(0, 0, ST, ST)
                .BackgroundColor(GlassIn).BorderColor(InputBd).BorderWidth(1).Rounded(9).Enter())
            {
                TabChip(P, Loc.Get("launcher.tab_recent"), 0);
                TabChip(P, Loc.Get("launcher.new_project"), 1);
            }
        }
    }

    // Language picker: globe + name + region chip + chevron trigger; rows show chip + name + a
    // checkmark on the current locale.
    private static void LanguageDropdown(Paper P, Scribe.FontFile font)
    {
        int cur = LocaleHelper.GetIndex(EditorSettings.Instance.Locale);
        var globe = new FontIcon(font, EditorIcons.Globe);

        var items = new int[LocaleHelper.Codes.Length];
        for (int i = 0; i < items.Length; i++) items[i] = i;

        Origami.Dropdown<int>(P, "pl_lang", cur, LocaleHelper.SetLocale, items)
            .Width(UnitValue.Auto).Height(34)
            .PopoverWidth(210).ItemHeight(30)
            .CustomTrigger(ctx =>
            {
                P.Box("pl_lang_ic").Width(20).Height(ST).Margin(12, 8, ST, ST).IsNotInteractable()
                    .Icon(P, globe, TMid, size: 15f);
                P.Box("pl_lang_nm").Width(UnitValue.Auto).Height(ST).Margin(0, 8, ST, ST).IsNotInteractable()
                    .Text(LocaleHelper.Names[cur], font).FontSize(13f * TS).TextColor(THi).Alignment(TextAlignment.MiddleLeft);
                LangChip(P, "pl_lang_tag", LocaleHelper.Tags[cur], font);
                P.Box("pl_lang_chev").Width(16).Height(ST).Margin(6, 12, ST, ST).IsNotInteractable()
                    .Icon(P, new FontIcon(font, ctx.IsOpen ? EditorIcons.ChevronUp : EditorIcons.ChevronDown), TMid, size: 10f);
            })
            .ItemRender((i, ctx) =>
            {
                LangChip(P, $"pl_lang_r{i}_tag", LocaleHelper.Tags[i], font);
                P.Box($"pl_lang_r{i}_nm").Width(ST).Height(ST).IsNotInteractable()
                    .Text(LocaleHelper.Names[i], font).FontSize(12.5f * TS)
                    .TextColor(ctx.IsSelected ? THi : TMid).Alignment(TextAlignment.MiddleLeft);
                if (ctx.IsSelected)
                    P.Box($"pl_lang_r{i}_ck").Width(16).Height(ST).IsNotInteractable()
                        .Icon(P, new FontIcon(font, EditorIcons.Check), Acc, size: 11f);
            })
            .Show();
    }

    // Small pill showing a language's region tag (e.g. "US"), sized to its text.
    private static void LangChip(Paper P, string id, string text, Scribe.FontFile font)
    {
        P.Box(id).Width(UnitValue.Auto).Height(17).Rounded(5).Margin(5, 5, ST, ST).Padding(6, 6, 0, 0)
            .BackgroundColor(Col(255, 255, 255, 0.06f)).BorderColor(BdSoft).BorderWidth(1)
            .IsNotInteractable()
            .Text(text, font).FontSize(9.5f * TS).TextColor(TMid).Alignment(TextAlignment.MiddleCenter);
    }

    private static void TabChip(Paper P, string label, int index)
    {
        bool on = _tab == index;
        float leftM = index == 0 ? 3 : 4;
        float rightM = index == 1 ? 3 : 0;
        P.Box("pl_tab" + index).Width(UnitValue.Auto).Height(28).Rounded(8)
            .Margin(leftM, rightM, ST, ST).Padding(14, 14, 0, 0)
            .BackgroundColor(on ? Acc : Color.Transparent)
            .Text(label, EditorTheme.FontMedium ?? EditorTheme.DefaultFont)
            .FontSize(13f * TS).TextColor(on ? EditorTheme.Ink700 : TMid).Alignment(TextAlignment.MiddleCenter)
            .OnClick(_ => _tab = index);
    }

    // ---- Recent tab -------------------------------------------------
    private static void RecentBody(Paper P, Scribe.FontFile font)
    {
        using (P.Column("pl_recent").Enter())
        {
            using (P.Row("pl_rbar").Height(58).Enter())
            {
                P.Box("pl_rtitle").Width(ST).Height(UnitValue.Auto).Margin(26, 0, ST, ST)
                    .Text(Loc.Get("launcher.recent_projects"), EditorTheme.FontSemiBold ?? font).FontSize(18f * TS).TextColor(THi).Alignment(TextAlignment.MiddleLeft);

                using (P.Box("pl_search_wrap").Width(220).Height(34).Margin(0, 0, ST, ST).Enter())
                    Origami.TextField(P, "pl_search", _search, v => _search = v)
                        .Search(Loc.Get("launcher.search"))
                        .Width(ST).Height(34).Show();

                using (P.Row("pl_open").Width(UnitValue.Auto).Height(34).Rounded(9).Margin(9, 26, ST, ST)
                    .BackgroundColor(GlassIn).BorderColor(InputBd).BorderWidth(1)
                    .Hovered.BorderColor(BdStrong).End()
                    .OnClick(_ => OpenProjectDialog())
                    .Enter())
                {
                    IconBox(P, "pl_oico", EditorIcons.FolderOpen_I, 15, TMid, 1.4f, 12);
                    P.Box("pl_otxt").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(7, 13, ST, ST)
                        .Text(Loc.Get("launcher.open"), font).FontSize(13 * TS).TextColor(TMid).Alignment(TextAlignment.MiddleLeft);
                }
            }

            var entries = RecentProjects.FavoritesFirst();
            bool searching = !string.IsNullOrWhiteSpace(_search);
            if (searching)
            {
                string q = _search.Trim();
                entries = entries.FindAll(e =>
                    e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.Path.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            Origami.ScrollView(P, "pl_cards_sv", 938, 555 - 58).Padding(22, 22, 4, 22).ColSpacing(7).Body(() =>
            {
                if (entries.Count == 0)
                {
                    P.Box("pl_empty").Height(120)
                        .Text(Loc.Get(searching ? "launcher.no_results" : "launcher.no_recent"), font).FontSize(14f * TS).TextColor(TMid).Alignment(TextAlignment.MiddleCenter);
                    return;
                }
                for (int i = 0; i < entries.Count; i++)
                    Card(P, font, entries[i], i);
            });
        }
    }

    private static void Card(Paper P, Scribe.FontFile font, RecentProjectEntry entry, int i)
    {
        bool exists = Directory.Exists(entry.Path);
        var mono = EditorTheme.FontMono ?? font;

        using (P.Row("pl_card" + i).Height(70).Rounded(11)
            .BackgroundColor(CardBg).BorderColor(BdSoft).BorderWidth(1)
            .Transition(GuiProp.BackgroundColor, 0.15f)
            .Transition(GuiProp.BorderColor, 0.15f)
            .Transition(GuiProp.TranslateX, 0.15f)
            .Transition(GuiProp.BoxShadow, 0.2f)
            .Hovered
                .BackgroundColor(EditorTheme.Hover)
                .BorderColor(BdStrong)
                .Translate(3, 0)
                .Glow(0, 10, 26, -6, Color.FromArgb(140, EditorTheme.Purple400))
                .End()
            .OnClick(entry, (e, _) => { if (Directory.Exists(e.Path)) TryOpenProject(e.Path); })
            .Enter())
        {
            RecentCardContextMenu(P, "pl_cardctx" + i, entry, exists);

            // Favorite toggle: outline star normally, filled yellow when favored. Drawn with the FA
            // weight directly (solid vs regular) because the fallback chain otherwise always resolves
            // the star to its outline. No background unless hovered; StopEventPropagation keeps the
            // click off the card's open-project handler.
            bool fav = entry.Favorite;
            Color amber = EditorTheme.Amber400;
            using (P.Box("pl_fav" + i).Width(44).Height(44).Rounded(12).Margin(16, 0, ST, ST)
                .Hovered.BackgroundColor(Col(255, 255, 255, 0.08f)).End()
                .StopEventPropagation()
                .OnClick(entry, (e, _) => RecentProjects.SetFavorite(e.Path, !e.Favorite))
                .Enter())
                P.Draw((vg, r) => DrawIcon(vg, r, fav ? _starSolid : _starOutline, 22, fav ? amber : Color.White, 1.5f));

            using (P.Column("pl_ci" + i).Width(ST).Height(UnitValue.Auto).Margin(15, 0, ST, ST).Enter())
            {
                using (P.Row("pl_cn" + i).Width(ST).Height(UnitValue.Auto).Enter())
                {
                    P.Box("pl_cname" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(0, 0, ST, ST)
                        .Text(entry.Name, EditorTheme.FontSemiBold ?? font).FontSize(15 * TS).TextColor(exists ? THi : TMid).Alignment(TextAlignment.MiddleLeft);

                    // Version chip (or a "missing" chip when the folder is gone).
                    if (exists)
                        P.Box("pl_cver" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Rounded(5).Margin(9, 0, ST, ST).Padding(7, 7, 3, 3)
                            .BackgroundColor(Raised)
                            .Text($"v{EngineVersion()}", mono).FontSize(10 * TS).TextColor(TMid).Alignment(TextAlignment.MiddleCenter);
                    else
                        P.Box("pl_cmiss" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Rounded(5).Margin(9, 0, ST, ST).Padding(7, 7, 3, 3)
                            .BackgroundColor(Color.FromArgb(128, EditorTheme.Red300))
                            .Text(Loc.Get("launcher.missing"), mono).FontSize(10 * TS).TextColor(EditorTheme.Red400).Alignment(TextAlignment.MiddleCenter);

                    P.Box("pl_cnsp" + i).Width(ST);
                }

                P.Box("pl_cp" + i).Width(ST).Height(UnitValue.Auto).Margin(0, 0, 5, 0)
                    .Text(entry.Path, mono).FontSize(11.5f * TS).TextColor(TLo).Alignment(TextAlignment.MiddleLeft);
            }

            P.Box("pl_ct" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(8, 0, ST, ST)
                .Text(FormatTimeAgo(entry.LastOpened), font).FontSize(12 * TS).TextColor(TMid).Alignment(TextAlignment.MiddleRight);

            // Arrow, revealed on hover
            Color arrowCol = Color.FromArgb(P.IsParentHovered ? 255 : 0, Acc300.R, Acc300.G, Acc300.B);
            using (P.Box("pl_carr" + i).Width(18).Height(18).Margin(10, 16, ST, ST).IsNotInteractable().Enter())
                P.Draw((vg, r) => DrawIcon(vg, r, EditorIcons.ArrowRight_I, 17, arrowCol, 1.6f));
        }
    }

    private static void RecentCardContextMenu(Paper P, string id, RecentProjectEntry entry, bool exists)
    {
        Origami.RightClickMenu(P, id, b =>
        {
            if (exists)
            {
                b.Item(Loc.Get("launcher.open"), () => TryOpenProject(entry.Path), icon: EditorIcons.FolderOpen);
                b.Item(Loc.Get("project.show_in_explorer"), () => EditorUtils.OpenFileSystemPath(entry.Path), icon: EditorIcons.FolderTree);
                b.Separator();
            }
            b.Item(Loc.Get("project.copy_path"), () => P.SetClipboard(entry.Path), icon: EditorIcons.Copy);
            b.Item(Loc.Get("launcher.remove_recent"), () => RecentProjects.Remove(entry.Path), icon: EditorIcons.Xmark);
            if (exists)
            {
                b.Separator();
                b.Item(Loc.Get("launcher.delete_project"), () => Origami.Confirm(
                    Loc.Get("launcher.delete_confirm_title"),
                    Loc.Get("launcher.delete_confirm_body", new { name = entry.Name }),
                    () =>
                    {
                        try { if (Directory.Exists(entry.Path)) Directory.Delete(entry.Path, true); }
                        catch (Exception ex) { Runtime.Debug.LogError($"Failed to delete project: {ex.Message}"); }
                        RecentProjects.Remove(entry.Path);
                    }), icon: EditorIcons.Trash, danger: true);
            }
        });
    }

    // ---- New Project tab --------------------------------------------
    private static void NewProjectBody(Paper P, Scribe.FontFile font)
    {
        using (P.Row("pl_new").Enter())
        {
            using (P.Column("pl_tplcol").Width(ST).Padding(26, 26, 20, 20).Enter())
            {
                P.Box("pl_tplhead").Height(UnitValue.Auto).Margin(0, 0, 0, 16)
                    .Text(Loc.Get("launcher.choose_template"), EditorTheme.FontSemiBold ?? font).FontSize(18f * TS).TextColor(THi).Alignment(TextAlignment.MiddleLeft);

                for (int r = 0; r < 2; r++)
                    using (P.Row("pl_tplrow" + r).Height(134).Margin(0, 0, r == 0 ? 0 : 12, 0).Enter())
                        for (int c = 0; c < 2; c++)
                        {
                            int idx = r * 2 + c;
                            if (idx == 0) BlankCard(P, font, c);
                            else EmptySlot(P, idx, c);
                        }
            }

            using (P.Column("pl_cfg").Width(308).Padding(24, 24, 24, 24)
                .BackgroundColor(Col(0, 0, 0, 0.18f)).BorderColor(BdSoft).BorderWidth(1).Enter())
            {
                P.Box("pl_cfghead").Height(UnitValue.Auto).Margin(0, 0, 0, 14)
                    .Text(Loc.Get("launcher.configure"), EditorTheme.FontSemiBold ?? font).FontSize(11f * TS).LetterSpacing(1f).TextColor(TLo).Alignment(TextAlignment.MiddleLeft);

                P.Box("pl_nmlbl").Height(UnitValue.Auto).Margin(0, 0, 0, 7)
                    .Text(Loc.Get("launcher.name"), font).FontSize(12f * TS).TextColor(TMid).Alignment(TextAlignment.MiddleLeft);
                Origami.TextField(P, "pl_nm", _newProjectName, v => _newProjectName = v)
                    .Width(ST).Height(38).Show();

                P.Box("pl_loclbl").Height(UnitValue.Auto).Margin(0, 0, 16, 7)
                    .Text(Loc.Get("launcher.path"), font).FontSize(12f * TS).TextColor(TMid).Alignment(TextAlignment.MiddleLeft);
                Origami.TextField(P, "pl_loc", _newProjectPath, v => _newProjectPath = v)
                    .Width(ST).Height(38).Mono()
                    .TrailingContent(() =>
                    {
                        using (P.Box("pl_locbtn").Width(24).Height(24).Margin(2, 6, ST, ST).Rounded(6)
                            .Hovered.BackgroundColor(Col(255, 255, 255, 0.07f)).End()
                            .OnClick(_ => BrowseLocation())
                            .Enter())
                            P.Draw((vg, r) => DrawIcon(vg, r, EditorIcons.FolderOpen_I, 14, TMid, 1.4f));
                    })
                    .Show();

                P.Box("pl_cfgsp").Height(ST);

                using (P.Row("pl_cta").Height(44).Rounded(11)
                    .BackgroundLinearGradient(0, 0, 1, 1, Acc, AccBright)
                    .Glow(0, 8, 26, -4, Color.FromArgb(153, EditorTheme.Purple400))
                    .OnClick(_ => TryCreateProject())
                    .Enter())
                {
                    P.Box("pl_ctal").Width(ST);
                    IconBox(P, "pl_ctaico", EditorIcons.Bolt_I, 16, EditorTheme.Ink700, 1.4f, 0);
                    P.Box("pl_ctatxt").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(8, 0, ST, ST)
                        .Text(Loc.Get("launcher.create_project"), EditorTheme.FontSemiBold ?? font).FontSize(14f * TS).TextColor(EditorTheme.Ink700).Alignment(TextAlignment.MiddleLeft);
                    P.Box("pl_ctar").Width(ST);
                }
            }
        }
    }

    // The only real template today (a blank project). Remaining grid cells are drawn as empty
    // placeholder slots to signal that more templates are coming.
    private static void BlankCard(Paper P, Scribe.FontFile font, int col)
    {
        (Color c1, Color c2) = GlyphColors(0.0, true);
        using (P.Column("pl_tpl0").Width(ST).Height(ST).Margin(col == 0 ? 0 : 6, col == 1 ? 0 : 6, 0, 0)
            .Rounded(12).Padding(17, 17, 17, 17)
            .BackgroundColor(EditorTheme.Selected)
            .BorderColor(Acc).BorderWidth(1.5f)
            .Enter())
        {
            using (P.Box("pl_tplico0").Width(46).Height(46).Rounded(12).Margin(0, 0, 0, 11)
                .BackgroundLinearGradient(0, 0, 1, 1, c1, c2).Enter())
                P.Draw((vg, r) => DrawIcon(vg, r, EditorIcons.FileLines_I, 24, EditorTheme.Ink700, 1.3f));

            P.Box("pl_tpln0").Height(UnitValue.Auto).Margin(0, 0, 0, 4)
                .Text(Loc.Get("launcher.tpl_blank_name"), EditorTheme.FontSemiBold ?? font).FontSize(15f * TS).TextColor(THi).Alignment(TextAlignment.MiddleLeft);
            P.Box("pl_tpld0").Height(UnitValue.Auto)
                .Text(Loc.Get("launcher.tpl_blank_desc"), font).FontSize(12f * TS).TextColor(TMid).Alignment(TextAlignment.MiddleLeft);
        }
    }

    // Placeholder for a future template: the card frame with no content inside.
    private static void EmptySlot(Paper P, int idx, int col)
    {
        using (P.Column("pl_tpl" + idx).Width(ST).Height(ST).Margin(col == 0 ? 0 : 6, col == 1 ? 0 : 6, 0, 0)
            .Rounded(12)
            .BackgroundColor(Col(255, 255, 255, 0.015f))
            .BorderColor(BdSoft).BorderWidth(1)
            .Enter())
        { }
    }

    // ---- helpers ----------------------------------------------------
    private static void IconBox(Paper P, string id, IOrigamiIcon icon, float size, Color color, float sw, float leftM, float rightM = 0)
    {
        using (P.Box(id).Width(size).Height(size).Margin(leftM, rightM, ST, ST).Enter())
            P.Draw((vg, r) => icon.Draw(vg, r, color, sw));
    }

    private static void DrawIcon(Canvas vg, Rect box, IOrigamiIcon icon, float size, Color color, float sw)
    {
        float ox = (float)(box.Min.X + (box.Size.X - size) / 2);
        float oy = (float)(box.Min.Y + (box.Size.Y - size) / 2);
        icon.Draw(vg, new Rect(new Float2(ox, oy), new Float2(ox + size, oy + size)), color, sw);
    }


    private static double HueFor(string name)
    {
        int hash = 0;
        foreach (char ch in name) hash = hash * 31 + ch;
        return ((hash % 360) + 360) % 360;
    }

    private static (Color, Color) GlyphColors(double hue, bool mono)
    {
        if (mono) return (Col(58, 53, 80), Col(36, 31, 56));
        return (Hsl(hue, 0.70, 0.64), Hsl(hue + 28, 0.62, 0.50));
    }

    private static Color Hsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue2(p, q, h + 1.0 / 3); g = Hue2(p, q, h); b = Hue2(p, q, h - 1.0 / 3);
        }
        return Col((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }
    private static double Hue2(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static void OpenProjectDialog()
    {
        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, path =>
        {
            if (path != null) TryOpenProject(path);
        });
    }

    private static void BrowseLocation()
    {
        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, path =>
        {
            if (path != null) _newProjectPath = path;
        }, _newProjectPath);
    }

    /// <summary>
    /// Draws the cycling tip strip at the bottom of the screen. Called separately from
    /// <see cref="Draw"/> so it can render on top of the intro animation while the project loads.
    /// </summary>
    public static void DrawTipStrip(Paper paper, float dt, float globalAlpha = 1f)
    {
        if (globalAlpha <= 0f) return;

        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        _tipTimer += dt;
        if (_tipTimer >= _tipDuration)
            AdvanceTip();

        float fadeIn = Math.Min(_tipTimer / _tipFadeTime, 1f);
        float fadeOut = Math.Min((_tipDuration - _tipTimer) / _tipFadeTime, 1f);
        float alpha = Math.Clamp(Math.Min(fadeIn, fadeOut), 0f, 1f) * Math.Clamp(globalAlpha, 0f, 1f);

        var iconBase = EditorTheme.Purple500;
        var textBase = EditorTheme.Ink400;
        var labelBase = EditorTheme.Ink300;
        var iconColor = Color.FromArgb((int)(iconBase.A * alpha), iconBase.R, iconBase.G, iconBase.B);
        var textColor = Color.FromArgb((int)(textBase.A * alpha), textBase.R, textBase.G, textBase.B);
        var labelColor = Color.FromArgb((int)(labelBase.A * alpha), labelBase.R, labelBase.G, labelBase.B);

        const float stripHeight = 36f;
        float y = h - stripHeight - 16f;
        string tipText = Loc.Get(_tipKeys[_tipIndex]);

        using (paper.Row("pl_tip_strip")
            .PositionType(PositionType.SelfDirected)
            .Position(0, y)
            .Size(w, stripHeight)
            .ChildLeft(UnitValue.StretchOne)
            .ChildRight(UnitValue.StretchOne)
            .RowBetween(6)
            .OnClick(_ => AdvanceTip())
            .Enter())
        {
            paper.Box("pl_tip_icon")
                .Width(20)
                .Height(stripHeight)
                .Text(EditorIcons.Lightbulb, font)
                .TextColor(iconColor)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);

            paper.Box("pl_tip_label")
                .Width(UnitValue.Auto)
                .Height(stripHeight)
                .Text(Loc.Get("launcher.tip_label"), font)
                .TextColor(labelColor)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pl_tip_text")
                .Width(UnitValue.Auto)
                .Height(stripHeight)
                .Text(tipText, font)
                .TextColor(textColor)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);
        }
    }

    private static void TryOpenProject(string path)
    {
        try
        {
            var project = Project.Open(path);
            project.SetActive();
            Close();
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError(Loc.Get("launcher.open_failed", new { message = ex.Message }));
        }
    }

    private static void TryCreateProject()
    {
        if (string.IsNullOrWhiteSpace(_newProjectName))
        {
            Toasts.Show(Loc.Get("launcher.invalid_name"), Loc.Get("launcher.name_empty"), ToastType.Warning, 3f);
            return;
        }

        string targetPath = Path.Combine(_newProjectPath, _newProjectName);
        if (Directory.Exists(targetPath) && Directory.GetFileSystemEntries(targetPath).Length > 0)
        {
            Toasts.Show(Loc.Get("launcher.folder_exists"), Loc.Get("launcher.folder_exists_msg", new { name = _newProjectName }), ToastType.Error, 5f);
            return;
        }

        try
        {
            var project = Project.Create(_newProjectPath, _newProjectName);
            project.SetActive();
            Close();
        }
        catch (Exception ex)
        {
            Toasts.Show(Loc.Get("launcher.create_failed"), ex.Message, ToastType.Error, 5f);
        }
    }

    private static string FormatTimeAgo(DateTime utcTime)
    {
        var span = DateTime.UtcNow - utcTime;
        if (span.TotalMinutes < 1) return Loc.Get("launcher.just_now");
        if (span.TotalHours < 1) return Loc.Get("launcher.minutes_ago", new { count = (int)span.TotalMinutes });
        if (span.TotalDays < 1) return Loc.Get("launcher.hours_ago", new { count = (int)span.TotalHours });
        if (span.TotalDays < 30) return Loc.Get("launcher.days_ago", new { count = (int)span.TotalDays });
        if (span.TotalDays < 365) return Loc.Get("launcher.months_ago", new { count = (int)(span.TotalDays / 30) });
        return Loc.Get("launcher.years_ago", new { count = (int)(span.TotalDays / 365) });
    }
}
