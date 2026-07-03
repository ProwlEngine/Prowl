using System;
using System.Collections.Generic;
using System.Linq;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace OrigamiSample
{
    // A docked panel with no content yet: a centred, muted title + subtitle. Used for every tab
    // except the Widget Playground, so the docking (splits, drag-out, tab bars) can be shown off.
    public sealed class EmptyPanel : DockPanel
    {
        private readonly string _title, _sub;
        private readonly FontFile _semi, _reg;
        public override string Title => _title;

        public EmptyPanel(string title, string sub, FontFile semi, FontFile reg)
        {
            _title = title; _sub = sub; _semi = semi; _reg = reg;
        }

        public override void OnGUI(Paper P, float w, float h)
        {
            using (P.Column("empty_" + _title).Width(P.Percent(100)).Height(P.Percent(100))
                .BackgroundColor(Palette.RootBg).Enter())
            {
                P.Box("es_t" + _title).Height(P.Stretch());
                P.Box("etitle" + _title).Width(P.Percent(100)).Height(P.Auto)
                    .Text(_title, _semi).FontSize(15 * Palette.TS).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleCenter);
                P.Box("esub" + _title).Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 5, 0)
                    .Text(_sub, _reg).FontSize(11.5f * Palette.TS).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleCenter);
                P.Box("es_b" + _title).Height(P.Stretch());
            }
        }
    }

    // The Widget Playground panel: top bar, category selector, and a responsive grid of empty widget
    // cards inside an Origami ScrollView. Card bodies fill in later as real Origami widgets.
    public sealed class WidgetPlaygroundPanel : DockPanel
    {
        private readonly FontFile _geist, _geistMed, _geistSemi, _mono;
        private int _cat;

        // live demo state
        private int _bgJoined = 1, _bgSeg = 1, _tabU, _tabP, _ddIdx = 1;
        private bool _tgA = true, _tgB, _tgCast = true, _tgTrig, _cbA = true, _cbInd = true, _rbA = true, _rbB;
        private float _sld1 = 0.65f, _sld2 = 0.4f, _rngLo = 0.25f, _rngHi = 0.72f;
        private List<string> _mddSel = new() { "Player", "Solid" };
        private int _tableSel = 0;
        private string _treeSel = "hero";
        private bool _foldA = true, _foldB;
        private DateTime _dpDate = new(2026, 6, 14);
        private DateTime _dpStart = new(2026, 6, 10), _dpEnd = new(2026, 6, 18);
        private string _tfName = "Character_Hero", _tfSearch = "", _tfBad = "!!bad name";
        private string _radioSel = "lin";
        private float _numMass = 72f, _numDrag = 1.0f;
        private Float2 _vec2 = new(12f, 4.5f);
        private Float3 _vec3 = new(-120.5f, 45.1f, 200f);
        private Float4 _vec4 = new(0f, 0f, 0f, 1f);
        private Prowl.Vector.Color _colorVal = new(168 / 255f, 85 / 255f, 247 / 255f, 1f);
        private readonly RigidbodyModel _rbModel = new();
        private PropertyGridConfig? _pgConfig;

        public override string Title => "Widget Playground";

        /// <summary>Select which category tab is shown (0 = All, 1..N = section index+1). Used by the --cat screenshot flag.</summary>
        public void SetCategory(int cat) => _cat = cat;

        public WidgetPlaygroundPanel(FontFile geist, FontFile geistMed, FontFile geistSemi, FontFile mono)
        {
            _geist = geist; _geistMed = geistMed; _geistSemi = geistSemi; _mono = mono;
        }

        private struct CardDef { public string Title, Tag; public int Span; public CardDef(string t, string g, int s = 1) { Title = t; Tag = g; Span = s; } }
        private struct Section { public string Name; public CardDef[] Cards; }

        private static readonly Section[] Sections =
        {
            new() { Name = "Navigation", Cards = new[]
            {
                new CardDef("Menu Bar", "app / bar", 2), new CardDef("Tabs", "underline / pills"), new CardDef("Breadcrumb", "path"),
            }},
            new() { Name = "Buttons & Actions", Cards = new[]
            {
                new CardDef("Button", "variants", 2), new CardDef("Button Group", "segmented"), new CardDef("Toggle", "switch"), new CardDef("Radio Group", "single"),
            }},
            new() { Name = "Inputs", Cards = new[]
            {
                new CardDef("Text Field", "input"), new CardDef("Numeric Field", "stepper"), new CardDef("Vector Fields", "v2/v3/v4"),
                new CardDef("Color Field", "picker"), new CardDef("Slider", "value"), new CardDef("Range Slider", "dual"), new CardDef("Date Picker", "calendar"),
            }},
            new() { Name = "Selection", Cards = new[]
            {
                new CardDef("Dropdown", "select"), new CardDef("Multi Dropdown", "tags"),
            }},
            new() { Name = "Overlays & Dialogs", Cards = new[]
            {
                new CardDef("Modal", "dialog"), new CardDef("File Dialog", "picker", 2), new CardDef("Context Menu", "right-click"),
            }},
            new() { Name = "Feedback", Cards = new[]
            {
                new CardDef("Progress Bar", "linear/ring"), new CardDef("Spinners", "3 styles"), new CardDef("Skeleton", "loading"),
                new CardDef("Toasts", "notify"), new CardDef("Tooltip", "hover"),
            }},
            new() { Name = "Data Display", Cards = new[]
            {
                new CardDef("Table", "data grid", 2), new CardDef("Image Diff", "compare"),
            }},
            new() { Name = "Structure & Content", Cards = new[]
            {
                new CardDef("Label", "typography", 2), new CardDef("Property Grid", "inspector", 2), new CardDef("Header", "label"),
                new CardDef("Foldout", "collapse"), new CardDef("Accordion", "stack"), new CardDef("Tree", "hierarchy"), new CardDef("Chat Bubble", "messages"),
            }},
        };

        public override void OnGUI(Paper P, float w, float h)
        {
            using (P.Column("pgroot").Width(P.Percent(100)).Height(P.Percent(100)).BackgroundColor(Palette.RootBg).Enter())
            {
                TopBar(P);
                P.Box("pgtopdiv").Height(1).BackgroundColor(Palette.BdSoft);
                NavBar(P);
                P.Box("pgnavdiv").Height(1).BackgroundColor(Palette.BdSoft);

                float scrollH = h - 46f - 34f - 2f;
                float contentW = w - 32f - 6f; // scroll padding + scrollbar
                var scrollEnv = Environment.GetEnvironmentVariable("PROWL_SCROLL");
                if (scrollEnv != null && float.TryParse(scrollEnv, out var scrollY))
                    Origami.ScrollTo("pgscroll", new Float2(0, scrollY));
                Origami.ScrollView(P, "pgscroll", w, scrollH).Padding(16, 16, 16, 16).Body(() =>
                {
                    if (_cat == 0)
                        for (int i = 0; i < Sections.Length; i++) SectionBlock(P, Sections[i], i, contentW, i == 0);
                    else
                        SectionBlock(P, Sections[_cat - 1], _cat - 1, contentW, true);
                });
            }
        }

        private void TopBar(Paper P)
        {
            using (P.Row("W2Top").Height(46).Padding(14, 14, 0, 0).Enter())
            {
                Icon(P, "w2topIco", SvgIcon.Grid3, 15, Palette.Acc300, 1.3f, 0);
                P.Box("w2topTitle").Width(P.Auto).Height(P.Auto).Margin(10, 0, P.Stretch(), P.Stretch())
                    .Text("Widget Playground", _geistSemi).FontSize(13 * Palette.TS).TextColor(Palette.THi).Alignment(TextAlignment.MiddleLeft);
                Tag(P, "w2topTag", "PROWL.UI - 33 components", 10);
                P.Box("w2topSpacer").Width(P.Stretch());
                using (P.Row("w2search").Width(170).Height(28).Rounded(8).Margin(0, 0, P.Stretch(), P.Stretch())
                    .BackgroundColor(Palette.GlassIn).BorderColor(Palette.BdSoft).BorderWidth(1).Enter())
                {
                    Icon(P, "w2searchIco", SvgIcon.Search, 12, Palette.TLo, 1.4f, 10);
                    P.Box("w2searchPh").Width(P.Stretch()).Height(P.Auto).Margin(6, 10, P.Stretch(), P.Stretch())
                        .Text("Search", _mono).FontSize(11.5f * Palette.TS).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleLeft);
                }
            }
        }

        private void NavBar(Paper P)
        {
            using (P.Row("W2Nav").Height(34).Padding(12, 12, 0, 0).Enter())
            {
                NavPill(P, "All", 0);
                for (int i = 0; i < Sections.Length; i++)
                    NavPill(P, Sections[i].Name, i + 1);
            }
        }

        private void NavPill(Paper P, string label, int index)
        {
            bool on = _cat == index;
            P.Box("nav" + index).Width(P.Auto).Height(24).Rounded(12).Margin(index == 0 ? 0 : 2, 0, P.Stretch(), P.Stretch()).Padding(11, 11, 0, 0)
                .BackgroundColor(on ? Palette.Acc : Palette.Transparent)
                .Transition(GuiProp.BackgroundColor, 0.12f)
                .Hovered.BackgroundColor(on ? Palette.Acc : Palette.Hover).End()
                .Text(label, _geistMed).FontSize(11.5f * Palette.TS).TextColor(on ? Palette.White : Palette.TMid).Alignment(TextAlignment.MiddleCenter)
                .OnClick(_ => _cat = index);
        }

        private void SectionBlock(Paper P, Section sec, int si, float contentW, bool first)
        {
            using (P.Column("sec" + si).Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, first ? 0 : 22, 0).Enter())
            {
                P.Box("secTitle" + si).Width(P.Auto).Height(P.Auto).Margin(2, 0, 6, 4)
                    .Text(sec.Name.ToUpperInvariant(), _geistSemi).FontSize(11 * Palette.TS).LetterSpacing(1.1f)
                    .TextColor(Palette.Acc300).Alignment(TextAlignment.MiddleLeft);

                const float gap = 14f;
                int cols = Math.Max(1, (int)MathF.Floor((contentW + gap) / (272f + gap)));
                float cardW = (contentW - gap * (cols - 1)) / cols;

                // Flex-wrap the cards: they flow onto new lines automatically and the block grows to fit.
                using (P.Row($"sec{si}_cards").Width(P.Percent(100)).Height(P.Auto).WrapContent().RowBetween(gap).Enter())
                {
                    for (int idx = 0; idx < sec.Cards.Length; idx++)
                    {
                        var card = sec.Cards[idx];
                        int span = Math.Min(card.Span, cols);
                        float cw = span == 2 ? cardW * 2 + gap : cardW;
                        Card(P, $"c{si}_{idx}", card, cw, 0, ResolveContent(P, card.Title));
                    }
                }
            }
        }

        private void Card(Paper P, string id, CardDef def, float width, float leftM, Action content)
        {
            using (P.Column(id).Width(width).Height(P.Auto).Margin(leftM, 0, 0, 0)
                .Rounded(12).Padding(14, 14, 14, 14)
                .BackgroundColor(Palette.CardBg).BorderColor(Palette.BdSoft).BorderWidth(1)
                .Transition(GuiProp.BackgroundColor, 0.14f)
                .Transition(GuiProp.BorderColor, 0.14f)
                .Hovered.BackgroundColor(Palette.CardBgHover).BorderColor(Palette.Bd).End()
                .Enter())
            {
                using (P.Row(id + "_h").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    P.Box(id + "_t").Width(P.Auto).Height(P.Auto).Margin(0, 0, P.Stretch(), P.Stretch())
                        .Text(def.Title, _geistSemi).FontSize(12.5f * Palette.TS).TextColor(Palette.THi).Alignment(TextAlignment.MiddleLeft);
                    Tag(P, id + "_tag", def.Tag, 8);
                    P.Box(id + "_hs").Width(P.Stretch());
                }

                if (content != null)
                {
                    P.Box(id + "_gap").Height(12);
                    content();
                }
                else
                {
                    P.Box(id + "_ph").Height(56); // empty placeholder body (filled in later)
                }
            }
        }

        private Action ResolveContent(Paper P, string title) => title switch
        {
            "Button" => () => ButtonDemo(P),
            "Button Group" => () => ButtonGroupDemo(P),
            "Toggle" => () => ToggleDemo(P),
            "Tabs" => () => TabsDemo(P),
            "Slider" => () => SliderDemo(P),
            "Range Slider" => () => RangeSliderDemo(P),
            "Dropdown" => () => DropdownDemo(P),
            "Multi Dropdown" => () => MultiDropdownDemo(P),
            "Breadcrumb" => () => BreadcrumbDemo(P),
            "Chat Bubble" => () => ChatBubbleDemo(P),
            "Spinners" => () => SpinnerDemo(P),
            "Skeleton" => () => SkeletonDemo(P),
            "Toasts" => () => ToastsDemo(P),
            "Tooltip" => () => TooltipDemo(P),
            "Table" => () => TableDemo(P),
            "Image Diff" => () => ImageDiffDemo(P),
            "Label" => () => LabelDemo(P),
            "Header" => () => HeaderDemo(P),
            "Foldout" => () => FoldoutDemo(P),
            "Accordion" => () => AccordionDemo(P),
            "Tree" => () => TreeDemo(P),
            "Text Field" => () => TextFieldDemo(P),
            "Radio Group" => () => RadioGroupDemo(P),
            "Numeric Field" => () => NumericFieldDemo(P),
            "Vector Fields" => () => VectorFieldDemo(P),
            "Color Field" => () => ColorFieldDemo(P),
            "Property Grid" => () => PropertyGridDemo(P),
            "Modal" => () => ModalDemo(P),
            "File Dialog" => () => FileDialogDemo(P),
            "Context Menu" => () => ContextMenuDemo(P),
            "Progress Bar" => () => ProgressBarDemo(P),
            "Date Picker" => () => DatePickerDemo(P),
            "Menu Bar" => () => MenuBarDemo(P),
            _ => null,
        };

        // ── Menu Bar / App Bar ───────────────────────────────────────────────────
        private void MenuBarDemo(Paper P)
        {
            using (P.Column("mbdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(14).Enter())
            {
                using (P.Column("mbsec1").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    DemoLabel(P, "mblab1", "Menu bar");
                    var mb = Origami.MenuBar(P, "mbbar");
                    foreach (var (label, build) in MenuBarMenus())
                        mb.Menu(label, build);
                    mb.Show();
                }

                using (P.Column("mbsec2").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    DemoLabel(P, "mblab2", "App bar");
                    Origami.AppBar(P, "appbar")
                        .Brand(IconMid(SvgIcon.Cube, Palette.White, 15), "Chimera")
                        .Tag("v0.1")
                        .Spacer()
                        .Action("search", IconMid(SvgIcon.Search, Palette.TMid, 15), () => { })
                        .Action("bolt", IconMid(SvgIcon.Bolt, Palette.TMid, 15), () => { })
                        .Action("gear", IconMid(SvgIcon.Gear, Palette.TMid, 15), () => { })
                        .Avatar("k", "K")
                        .Show();
                }
            }
        }

        private (string Label, Action<ContextBuilder> Build)[] MenuBarMenus() => new (string, Action<ContextBuilder>)[]
        {
            ("File", b => b
                .Item("New Scene", () => { }, iconDraw: IconDraw(SvgIcon.Plus, Palette.TMid), shortcut: "Ctrl N")
                .Item("Open Scene", () => { }, iconDraw: IconDraw(SvgIcon.FolderOpen, Palette.TMid), shortcut: "Ctrl O")
                .Submenu("Open Recent", s => s
                    .Item("Planet.scene", () => { })
                    .Item("Terrain.scene", () => { })
                    .Item("Lighting.scene", () => { })
                    .Separator()
                    .Item("Clear Recent", () => { }))
                .Separator()
                .Item("Save", () => { }, iconDraw: IconDraw(SvgIcon.Doc, Palette.TMid), shortcut: "Ctrl S")
                .Item("Save As...", () => { }, shortcut: "Ctrl Shift S")
                .Separator()
                .Item("Exit", () => { }, shortcut: "Alt F4")),

            ("Edit", b => b
                .Item("Undo", () => { }, shortcut: "Ctrl Z")
                .Item("Redo", () => { }, enabled: false, shortcut: "Ctrl Y")
                .Separator()
                .Item("Cut", () => { }, shortcut: "Ctrl X")
                .Item("Copy", () => { }, iconDraw: IconDraw(SvgIcon.Layers, Palette.TMid), shortcut: "Ctrl C")
                .Item("Paste", () => { }, shortcut: "Ctrl V")
                .Separator()
                .Item("Project Settings", () => { }, iconDraw: IconDraw(SvgIcon.Gear, Palette.TMid))),

            ("Assets", b => b
                .Submenu("Create", s => s
                    .Item("Folder", () => { }, iconDraw: IconDraw(SvgIcon.Folder, Palette.C(251, 191, 36)))
                    .Item("Material", () => { }, iconDraw: IconDraw(SvgIcon.Material, Palette.C(217, 107, 216)))
                    .Item("Script", () => { }, iconDraw: IconDraw(SvgIcon.Script, Palette.C(74, 222, 128)))
                    .Item("Shader", () => { }))
                .Item("Import New Asset...", () => { }, iconDraw: IconDraw(SvgIcon.FolderOpen, Palette.TMid))
                .Item("Refresh", () => { }, shortcut: "Ctrl R")
                .Separator()
                .Item("Show in Explorer", () => { }, iconDraw: IconDraw(SvgIcon.Link, Palette.TMid))),

            ("GameObject", b => b
                .Item("Create Empty", () => { }, iconDraw: IconDraw(SvgIcon.Cube, Palette.Acc300))
                .Submenu("3D Object", s => s
                    .Item("Cube", () => { })
                    .Item("Sphere", () => { })
                    .Item("Plane", () => { })
                    .Item("Capsule", () => { }))
                .Submenu("Light", s => s
                    .Item("Directional", () => { })
                    .Item("Point", () => { })
                    .Item("Spot", () => { }))
                .Separator()
                .Item("Camera", () => { })),

            ("Window", b => b
                .Submenu("Layouts", s => s
                    .Item("Default", () => { })
                    .Item("Tall", () => { })
                    .Item("Wide", () => { }))
                .Separator()
                .Item("Inspector", () => { })
                .Item("Hierarchy", () => { })
                .Item("Console", () => { })),

            ("Help", b => b
                .Item("Documentation", () => { }, iconDraw: IconDraw(SvgIcon.Link, Palette.TMid))
                .Item("About Prowl", () => { }, iconDraw: IconDraw(SvgIcon.Cube, Palette.Acc300))),
        };

        // A vector icon centered at `size` within whatever rect it's handed (for widget icon slots).
        private static Action<Canvas, Rect> IconMid(string path, Color color, float size) => (vg, r) =>
        {
            float ox = (float)(r.Min.X + (r.Size.X - size) / 2);
            float oy = (float)(r.Min.Y + (r.Size.Y - size) / 2);
            SvgIcon.Draw(vg, path, ox, oy, size, color, 1.6f);
        };

        // ── Slider ───────────────────────────────────────────────────────────────
        private void SliderDemo(Paper P)
        {
            using (P.Column("slddemo").Width(P.Percent(100)).Height(P.Auto).Enter())
            {
                DemoLabel(P, "sll1", "Value 0-1");
                using (P.Row("slr1").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 12, 0).Enter())
                    Origami.Slider(P, "sld1", _sld1, v => _sld1 = v, 0f, 1f).ShowValue().Format("0.00").TrackThickness(4).ThumbSize(12).Width(P.Stretch()).Show();

                DemoLabel(P, "sll2", "Percentage");
                using (P.Row("slr2").Width(P.Percent(100)).Height(P.Auto).Enter())
                    Origami.Slider(P, "sld2", _sld2, v => _sld2 = v, 0f, 1f).ShowValue().Format("0%").TrackThickness(4).ThumbSize(12).Width(P.Stretch()).Show();
            }
        }

        // ── Range Slider ─────────────────────────────────────────────────────────
        private void RangeSliderDemo(Paper P)
        {
            using (P.Column("rngdemo").Width(P.Percent(100)).Height(P.Auto).Enter())
            {
                using (P.Row("rngr1").Width(P.Percent(100)).Height(P.Auto).Enter())
                    Origami.RangeSlider(P, "rng1", _rngLo, _rngHi, (lo, hi) => { _rngLo = lo; _rngHi = hi; }, 0f, 1f)
                        .ShowValue().Format("0.00").TrackThickness(4).ThumbSize(12).Width(P.Stretch()).Show();
            }
        }

        // ── Dropdown ─────────────────────────────────────────────────────────────
        private void DropdownDemo(Paper P)
        {
            using (P.Column("dddemo").Width(P.Percent(100)).Height(P.Auto).Enter())
                Origami.Dropdown(P, "dd1", _ddIdx, i => _ddIdx = i, new[] { "None", "Interpolate", "Extrapolate" })
                    .Width(P.Stretch()).Height(32).Show();
        }

        // ── Multi Dropdown ───────────────────────────────────────────────────────
        private void MultiDropdownDemo(Paper P)
        {
            using (P.Column("mdddemo").Width(P.Percent(100)).Height(P.Auto).Enter())
                Origami.MultiDropdown(P, "mdd1", _mddSel, list => _mddSel = new List<string>(list),
                        new[] { "Default", "Player", "Solid", "Water", "UI" })
                    .Placeholder("Add layer...").Height(32).Width(P.Stretch()).Show();
        }

        // ── Breadcrumb ───────────────────────────────────────────────────────────
        private void BreadcrumbDemo(Paper P)
        {
            var items = new[]
            {
                new BreadcrumbItem("Assets"), new BreadcrumbItem("Models"),
                new BreadcrumbItem("Planet"), new BreadcrumbItem("PlanetGen_Mesh"),
            };
            var pathItems = new[]
            {
                new BreadcrumbItem("~", IconDraw(SvgIcon.Folder, Palette.C(251, 191, 36))),
                new BreadcrumbItem("prowl"), new BreadcrumbItem("chimera"),
            };
            using (P.Column("bcdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(12).Enter())
            {
                Origami.Breadcrumb(P, "bc1", items, _ => { }).Chevrons().Show();
                Origami.Breadcrumb(P, "bc2", pathItems, _ => { }).Chevrons().ShowIcons().Show();
            }
        }

        // ── Chat Bubble ──────────────────────────────────────────────────────────
        private void ChatBubbleDemo(Paper P)
        {
            using (P.Column("cbdemo").Width(P.Percent(100)).Height(P.Auto).Enter())
            {
                Origami.ChatBubble(P, "cb1", p => p.Box("cb1t").Width(170).Height(P.Auto)
                        .Text("Did the atmosphere shader compile?", _geist).FontSize(12.5f * Palette.TS).TextColor(Palette.THi).Alignment(TextAlignment.Left).Wrap(TextWrapMode.Wrap))
                    .Avatar("A", Palette.C(96, 165, 250), 26).MaxWidth(210).Footer("Aria - 2:14 PM").TailLeft().Show();

                P.Box("cbg1").Width(P.Percent(100)).Height(8);
                Origami.ChatBubble(P, "cb2", p => p.Box("cb2t").Width(P.Auto).Height(P.Auto)
                        .Text("Yes - 0 errors, hot-reloaded", _geist).FontSize(12.5f * Palette.TS).TextColor(Palette.White).Alignment(TextAlignment.Left))
                    .Primary().MaxWidth(210).Footer("You - 2:15 PM").TailRight().Show();

                P.Box("cbg2").Width(P.Percent(100)).Height(8);
                // Typing indicator bubble (bouncing dots).
                Origami.ChatBubble(P, "cb3", p => Origami.Spinner(p, "cb3d").Dots().Diameter(20).Show())
                    .Avatar("A", Palette.C(96, 165, 250), 26).MaxWidth(210).TailLeft().Show();
            }
        }

        // ── Spinners ─────────────────────────────────────────────────────────────
        private void SpinnerDemo(Paper P)
        {
            using (P.Row("spdemo").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 4, 0).Enter())
            {
                SpinnerCol(P, "spring", "Ring", b => b.Ring());
                P.Box("spg1").Width(22);
                SpinnerCol(P, "spdots", "Dots", b => b.Dots());
                P.Box("spg2").Width(22);
                SpinnerCol(P, "spbars", "Bars", b => b.Bars());
            }
        }

        private void SpinnerCol(Paper P, string id, string label, Func<SpinnerBuilder, SpinnerBuilder> cfg)
        {
            using (P.Column(id).Width(P.Auto).Height(P.Auto).Enter())
            {
                using (P.Box(id + "_s").Size(24).Margin(P.Stretch(), P.Stretch(), 0, 6).Enter())
                    cfg(Origami.Spinner(P, id + "_sp")).Diameter(22).Show();
                P.Box(id + "_l").Width(P.Percent(100)).Height(P.Auto)
                    .Text(label, _geist).FontSize(10f * Palette.TS).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleCenter);
            }
        }

        // ── Table ────────────────────────────────────────────────────────────────
        private void TableDemo(Paper P)
        {
            var t = Origami.Table(P, "tbl", _tableSel, i => _tableSel = i)
                .Column("Name", 1.7f, sortable: true).Column("Type", 1f).Column("Size", 0.8f).Column("Modified", 1f);
            t.Row().Cell("PlanetGen_Mesh", Palette.THi, IconDraw(SvgIcon.Cube, Palette.C(168, 85, 247)))
                .Cell("Mesh", Palette.TMid).Cell("2.4 MB", Palette.TLo).Cell("2m ago", Palette.TLo);
            t.Row().Cell("Atmosphere_M", Palette.THi, IconDraw(SvgIcon.Material, Palette.C(217, 107, 216)))
                .Cell("Material", Palette.TMid).Cell("512 KB", Palette.TLo).Cell("1h ago", Palette.TLo);
            t.Row().Cell("PlanetGenerator", Palette.THi, IconDraw(SvgIcon.Script, Palette.C(74, 222, 128)))
                .Cell("Script", Palette.TMid).Cell("8 KB", Palette.TLo).Cell("1h ago", Palette.TLo);
            t.Row().Cell("Grass_Albedo", Palette.THi, IconDraw(SvgIcon.Layers, Palette.C(96, 165, 250)))
                .Cell("Texture", Palette.TMid).Cell("4.0 MB", Palette.TLo).Cell("yesterday", Palette.TLo);
            t.Show();
        }

        // ── Tree ─────────────────────────────────────────────────────────────────
        private void TreeDemo(Paper P)
        {
            Action<Canvas, Rect> folder = IconDraw(SvgIcon.Folder, Palette.C(251, 191, 36));
            var nodes = new List<TreeNode>
            {
                new() { Id = "qp", Label = "Quadtree Planet", Depth = 0, HasChildren = true, DefaultExpanded = true, IconDraw = IconDraw(SvgIcon.Globe, Palette.Acc) },
                new() { Id = "hero", Label = "Character_Hero", Depth = 1, IsLeaf = true, IconDraw = IconDraw(SvgIcon.Cube, Palette.C(96, 165, 250)) },
                new() { Id = "env", Label = "Environment", Depth = 1, HasChildren = true, DefaultExpanded = true, IconDraw = folder },
                new() { Id = "props", Label = "Props", Depth = 2, IsLeaf = true, IconDraw = folder },
                new() { Id = "anim", Label = "Animaster", Depth = 1, IsLeaf = true, IconDraw = IconDraw(SvgIcon.Bolt, Palette.C(74, 222, 128)) },
            };
            Origami.Tree(P, "hierarchy", 240f, 176f).Nodes(nodes)
                .IsSelected(n => n.Id == _treeSel).OnSelect(e => _treeSel = e.Node.Id).Show();
        }

        // ── Label ────────────────────────────────────────────────────────────────
        private void LabelDemo(Paper P)
        {
            using (P.Column("lbdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(5).Enter())
            {
                Origami.Label(P, "lbh", "Heading").Heading().Show();
                Origami.Label(P, "lbs", "Subheading").Subheading().Show();
                Origami.Label(P, "lbb", "Body text - the default paragraph label.").Body().Show();
                Origami.Label(P, "lbc", "Caption - secondary muted text").Caption().Show();

                P.Box("lbdiv").Width(P.Percent(100)).Height(1).Margin(0, 0, 4, 4).BackgroundColor(Palette.BdSoft);

                using (P.Row("lbfr").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.Label(P, "lbf1", "Field Label").FieldLabel().Show();
                    P.Box("lbfg").Width(18);
                    Origami.Label(P, "lbf2", "Required *").FieldLabel().Show();
                }

                using (P.Row("lblr").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 4, 0).Enter())
                {
                    Origami.Label(P, "lblink", "Link label").Link().Show();
                    P.Box("lblg1").Width(16);
                    Origami.Label(P, "lbdis", "Disabled").Disabled().Show();
                    P.Box("lblg2").Width(16);
                    Origami.Label(P, "lbico", "With icon").LeadingIcon(IconDraw(SvgIcon.Bolt, Palette.Acc300), 14).Show();
                    P.Box("lblg3").Width(16);
                    Origami.Label(P, "lbcode", "code_label").Code().Show();
                }

                using (P.Row("lbsr").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.Label(P, "lbsucc", "Success").Success().Dot().Show();
                    P.Box("lbsg1").Width(14);
                    Origami.Label(P, "lbwarn", "Warning").Warning().Dot().Show();
                    P.Box("lbsg2").Width(14);
                    Origami.Label(P, "lberr", "Error").Danger().Dot().Show();
                    P.Box("lbsg3").Width(14);
                    Origami.Label(P, "lbinfo", "Info").Info().Dot().Show();
                }

                P.Box("lbdiv2").Width(P.Percent(100)).Height(1).Margin(0, 0, 4, 4).BackgroundColor(Palette.BdSoft);

                using (P.Row("lbkv1").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.Label(P, "lbkv1k", "Vertices").Body().TextColor(Palette.TMid).Show();
                    P.Box("lbkv1s").Width(P.Stretch());
                    Origami.Label(P, "lbkv1v", "12,480").Body().TextColor(Palette.THi).Show();
                }
                using (P.Row("lbkv2").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.Label(P, "lbkv2k", "Status").Body().TextColor(Palette.TMid).Show();
                    P.Box("lbkv2s").Width(P.Stretch());
                    Origami.Label(P, "lbkv2v", "Baked").Success().Dot().Show();
                }

                DemoLabel(P, "lbtl", "Truncated");
                Origami.Label(P, "lbtrunc", "PlanetGenerator_HighDetail_LOD0_Final.mesh").Body().Width(190).Ellipsis().Show();
            }
        }

        // ── Header ───────────────────────────────────────────────────────────────
        private void HeaderDemo(Paper P)
        {
            using (P.Column("hddemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(12).Enter())
            {
                using (P.Column("hdb1").Width(P.Percent(100)).Height(P.Auto).ColBetween(4).Enter())
                {
                    DemoLabel(P, "hdl1", "Component / Foldout header");
                    Origami.Header(P, "hdcomp", "Rigidbody").Component()
                        .Chevron(true).IconDraw(IconDraw(SvgIcon.Cube, Palette.Acc300)).Checkbox(true).More().Show();
                }
                using (P.Column("hdb2").Width(P.Percent(100)).Height(P.Auto).ColBetween(4).Enter())
                {
                    DemoLabel(P, "hdl2", "Section label");
                    Origami.Header(P, "hdsec", "Physics").Show();
                }
            }
        }

        // ── Foldout ──────────────────────────────────────────────────────────────
        private void FoldoutDemo(Paper P)
        {
            using (P.Column("fldemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(7).Enter())
            {
                Origami.Foldout(P, "fldA", "Transform").Expanded(_foldA, v => _foldA = v)
                    .Icon(IconDraw(SvgIcon.Cube, Palette.Acc300))
                    .Body(() => Origami.Label(P, "fldA_b", "Position, Rotation & Scale fields").Body().TextColor(Palette.TMid).Show());
                Origami.Foldout(P, "fldB", "Advanced").Expanded(_foldB, v => _foldB = v)
                    .Body(() => Origami.Label(P, "fldB_b", "Collapsed section content.").Body().TextColor(Palette.TMid).Show());
            }
        }

        // ── Accordion (single-open stack of foldouts) ────────────────────────────
        private void AccordionDemo(Paper P)
        {
            (string id, string title, string icon, string body)[] items =
            {
                ("mesh", "Mesh Renderer", SvgIcon.Cube, "Casts shadows - 2 materials"),
                ("mat", "Material", SvgIcon.Material, "Albedo, Metallic & Normal maps"),
                ("coll", "Collider", SvgIcon.Layers, "Convex - Physics layer 3"),
            };
            var acc = Origami.Accordion(P, "acdemo").DefaultOpen("mesh");
            foreach (var it in items)
            {
                string bid = it.id, body = it.body;
                acc.Section(it.id, it.title, IconDraw(it.icon, Palette.Acc300),
                    () => Origami.Label(P, "accb_" + bid, body).Body().TextColor(Palette.TMid).Show());
            }
            acc.Show();
        }

        // ── Skeleton ─────────────────────────────────────────────────────────────
        private void SkeletonDemo(Paper P)
        {
            using (P.Column("skdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(11).Enter())
            {
                using (P.Row("skr1").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.Skeleton(P, "ska").Avatar(38).Show();
                    P.Box("skg").Width(12);
                    using (P.Column("skc").Width(P.Auto).Height(P.Auto).ColBetween(7).Margin(0, 0, P.Stretch(), P.Stretch()).Enter())
                    {
                        Origami.Skeleton(P, "skl1").Pill().Size(150, 11).Show();
                        Origami.Skeleton(P, "skl2").Pill().Size(96, 11).Show();
                    }
                }
                Origami.Skeleton(P, "skblock").Rect().Size(220, 60).Show();
            }
        }

        // ── Toasts ───────────────────────────────────────────────────────────────
        private void ToastsDemo(Paper P)
        {
            using (P.Column("todemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(10).Enter())
            {
                using (P.Row("tor1").Width(P.Percent(100)).Height(P.Auto).Enter())
                    Origami.Button(P, "toTrig", "Trigger toast").Primary().LeadingIcon(IconDraw(SvgIcon.Bolt, Palette.White))
                        .OnClick(() => Origami.Toast("Scene saved").Message("just now").Success().Show()).Show();

                DemoLabel(P, "tol", "Variants");
                Toasts.Preview(P, "toV1", "Scene saved", ToastType.Success);
                Toasts.Preview(P, "toV2", "Compile failed", ToastType.Error);
                Toasts.Preview(P, "toV3", "Reimported 4 assets", ToastType.Info);
            }
        }

        // ── Tooltip ──────────────────────────────────────────────────────────────
        private void TooltipDemo(Paper P)
        {
            using (P.Column("tipdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(10).Enter())
            {
                DemoLabel(P, "tipl", "Hover to preview");
                using (P.Row("tipr").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.Button(P, "tipb1", "Export").Primary().Tooltip("Save the current scene to disk").OnClick(() => { }).Show();
                    P.Box("tipg1").Width(8);
                    Origami.Button(P, "tipb2", "Rebuild").Tooltip("Regenerate the planet mesh").OnClick(() => { }).Show();
                    P.Box("tipg2").Width(8);
                    Origami.Button(P, "tipb3", "").IconOnly().LeadingIcon(IconDraw(SvgIcon.Gear, Palette.THi)).Tooltip("Settings").Show();
                }
            }
        }

        // ── Image Diff ───────────────────────────────────────────────────────────
        private object _diffA, _diffB;
        private void ImageDiffDemo(Paper P)
        {
            EnsureDiffTextures(P);
            using (P.Column("iddemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(10).Enter())
            {
                Origami.ImageDiff(P, "idiff", _diffA, _diffB).Width(P.Percent(100)).Height(156).Show();
                using (P.Row("idcap").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    P.Box("idcapL").Width(P.Auto).Height(P.Auto)
                        .Text("Baseline vs. current bake", _geist).FontSize(11f * Palette.TS).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleLeft);
                    P.Box("idcapS").Width(P.Stretch());
                    P.Box("idcapR").Width(P.Auto).Height(P.Auto)
                        .Text("delta 4.2%", _mono).FontSize(11f * Palette.TS).TextColor(Palette.Acc300).Alignment(TextAlignment.MiddleRight);
                }
            }
        }

        private void EnsureDiffTextures(Paper P)
        {
            if (_diffA != null) return;
            const int W = 160, H = 90;
            var a = new byte[W * H * 4];
            var b = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = (y * W + x) * 4;
                    // Before: #1b1630 -> #2a1e52 diagonal gradient + faint diagonal stripes.
                    float g = (x + y) / (float)(W + H);
                    bool stripe = (((x + y) / 8) & 1) == 0;
                    float sa = stripe ? 0.05f : 0f;
                    a[i + 0] = (byte)(Lerp(27, 42, g) + sa * 255);
                    a[i + 1] = (byte)(Lerp(22, 30, g) + sa * 255);
                    a[i + 2] = (byte)(Lerp(48, 82, g) + sa * 255);
                    a[i + 3] = 255;
                    // After: radial #7c3fd6 -> #a855f7 (42%) -> #d96bd8 (82%) from (0.32,0.22) + brighter stripes.
                    float dx = x / (float)W - 0.32f, dy = y / (float)H - 0.22f;
                    float d = MathF.Min(1f, MathF.Sqrt(dx * dx + dy * dy) / 0.9f);
                    float br, bg2, bb;
                    if (d < 0.42f) { float u = d / 0.42f; br = Lerp(124, 168, u); bg2 = Lerp(63, 85, u); bb = Lerp(214, 247, u); }
                    else { float u = MathF.Min(1f, (d - 0.42f) / 0.4f); br = Lerp(168, 217, u); bg2 = Lerp(85, 107, u); bb = Lerp(247, 216, u); }
                    float sb = stripe ? 0.12f : 0f;
                    b[i + 0] = (byte)MathF.Min(255, br + sb * 255);
                    b[i + 1] = (byte)MathF.Min(255, bg2 + sb * 255);
                    b[i + 2] = (byte)MathF.Min(255, bb + sb * 255);
                    b[i + 3] = 255;
                }
            _diffA = P.Renderer.CreateTexture(W, H);
            P.Renderer.SetTextureData(_diffA, new IntRect(0, 0, W, H), a);
            _diffB = P.Renderer.CreateTexture(W, H);
            P.Renderer.SetTextureData(_diffB, new IntRect(0, 0, W, H), b);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ── Text Field ───────────────────────────────────────────────────────────
        private void TextFieldDemo(Paper P)
        {
            using (P.Column("tfdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(8).Enter())
            {
                DemoLabel(P, "tfl1", "Default");
                Origami.TextField(P, "tf1", _tfName, v => _tfName = v).Mono().Show();
                DemoLabel(P, "tfl2", "With icon + placeholder");
                Origami.TextField(P, "tf2", _tfSearch, v => _tfSearch = v).Search("Search assets...").Show();
                DemoLabel(P, "tfl3", "Error state");
                Origami.TextField(P, "tf3", _tfBad, v => _tfBad = v).Mono()
                    .Error("Name cannot contain special characters").Show();
            }
        }

        // ── Radio Group ──────────────────────────────────────────────────────────
        private void RadioGroupDemo(Paper P)
        {
            Origami.RadioGroup(P, "rgd", _radioSel, v => _radioSel = v, new[] { "lin", "gam", "hdr" })
                .Display(id => id == "lin" ? "Linear" : id == "gam" ? "Gamma" : "HDR (disabled)")
                .IsItemEnabled(id => id != "hdr")
                .Gap(9).Show();
        }

        // ── Numeric Field ────────────────────────────────────────────────────────
        private void NumericFieldDemo(Paper P)
        {
            using (P.Column("nfdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(8).Enter())
            {
                DemoLabel(P, "nfl1", "Stepper");
                Origami.NumericField<float>(P, "nf1", _numMass, v => _numMass = v)
                    .Suffix("kg").Stepper().Format("F1").Show();
                DemoLabel(P, "nfl2", "Draggable label");
                Origami.NumericField<float>(P, "nf2", _numDrag, v => _numDrag = v)
                    .DraggableLabel("Mass", Palette.Acc300).Format("F2").Show();
            }
        }

        // ── Vector Fields ────────────────────────────────────────────────────────
        private void VectorFieldDemo(Paper P)
        {
            using (P.Column("vfdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(8).Enter())
            {
                DemoLabel(P, "vfl2", "Vector2");
                Origami.Float2Field(P, "vf2", _vec2, v => _vec2 = v).Show();
                DemoLabel(P, "vfl3", "Vector3");
                Origami.Float3Field(P, "vf3", _vec3, v => _vec3 = v).Show();
                DemoLabel(P, "vfl4", "Vector4 / Quaternion");
                Origami.Float4Field(P, "vf4", _vec4, v => _vec4 = v).Show();
            }
        }

        // ── Color Field ──────────────────────────────────────────────────────────
        private void ColorFieldDemo(Paper P)
        {
            (int r, int g, int b)[] sw =
            {
                (168, 85, 247), (217, 107, 216), (96, 165, 250), (74, 222, 128), (251, 191, 36), (251, 113, 133),
            };
            int cr = (int)MathF.Round(_colorVal.R * 255), cg = (int)MathF.Round(_colorVal.G * 255), cb = (int)MathF.Round(_colorVal.B * 255);

            using (P.Column("cfdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(8).Enter())
            {
                Origami.ColorField(P, "cf1", _colorVal, v => _colorVal = v).Show();

                DemoLabel(P, "cfl", "Swatch palette");
                using (P.Row("cfsw").Width(P.Percent(100)).Height(P.Auto).RowBetween(5).Enter())
                {
                    foreach (var s in sw)
                    {
                        var sc = s;
                        bool on = cr == sc.r && cg == sc.g && cb == sc.b;
                        P.Box($"cfsw_{sc.r}_{sc.g}_{sc.b}").Width(24).Height(24).Rounded(7)
                            .BackgroundColor(Palette.C(sc.r, sc.g, sc.b))
                            .BorderColor(on ? Palette.White : Palette.C(255, 255, 255, 0.12f)).BorderWidth(on ? 2 : 1)
                            .OnClick(_ => _colorVal = new Prowl.Vector.Color(sc.r / 255f, sc.g / 255f, sc.b / 255f, 1f));
                    }
                    P.Box("cfsw_fill").Width(P.Stretch());
                }

                Color32[] hue =
                {
                    new(251, 113, 133, 255), new(251, 191, 36, 255), new(74, 222, 128, 255), new(52, 211, 238, 255),
                    new(96, 165, 250, 255), new(168, 85, 247, 255), new(251, 113, 133, 255),
                };
                P.Box("cfhue").Width(P.Percent(100)).Height(12).Rounded(5).IsNotInteractable()
                    .OnPostLayout((h, r) => P.Draw(ref h, (canvas, rr) =>
                    {
                        float x = (float)rr.Min.X, y = (float)rr.Min.Y, w = (float)rr.Size.X, ht = (float)rr.Size.Y;
                        int segs = hue.Length - 1; float sw2 = w / segs;
                        for (int i = 0; i < segs; i++)
                        {
                            canvas.SetLinearBrush(x + i * sw2, y, x + (i + 1) * sw2, y, hue[i], hue[i + 1]);
                            canvas.RectFilled(x + i * sw2, y, sw2 + 1, ht, new Color32(255, 255, 255, 255));
                            canvas.ClearBrush();
                        }
                    }));
            }
        }

        // ── Property Grid (hand-composed .pg, reusing widgets) ───────────────────
        private void PropertyGridDemo(Paper P)
        {
            // Reflection-driven inspector: the grid reflects _rbModel's fields and picks a control per
            // type (bool -> switch, Float3 -> vector field, Color -> color field, enum -> dropdown), plus
            // nested objects (foldouts), lists (add/remove/reorder), [Header] sections, [Range] sliders
            // and [Button] methods - all from the one object, no per-field wiring.
            using (P.Column("pgcard").Width(P.Percent(100)).Height(P.Auto).Rounded(9).Clip()
                .BorderColor(Palette.BdSoft).BorderWidth(1).Enter())
            {
                using (P.Row("pghead").Width(P.Percent(100)).Height(P.Auto).Padding(11, 11, 8, 8)
                    .BackgroundColor(Palette.GlassIn).RoundedTop(9).Enter())
                {
                    Icon(P, "pghico", SvgIcon.Cube, 13, Palette.Acc300, 1.5f, 0);
                    P.Box("pght").Width(P.Auto).Height(P.Auto).Margin(9, 0, P.Stretch(), P.Stretch())
                        .Text("Rigidbody", _geistSemi).FontSize(12 * Palette.TS).TextColor(Palette.THi).Alignment(TextAlignment.MiddleLeft);
                }
                P.Box("pghdiv").Width(P.Percent(100)).Height(1).BackgroundColor(Palette.BdSoft);

                using (P.Column("pgbody").Width(P.Percent(100)).Height(P.Auto).Padding(0, 0, 4, 6).Enter())
                    Origami.PropertyGrid(P, "pgrid", _rbModel, PgConfig()).Show();
            }
        }

        // One-time config: built-in drawers for every primitive/vector/color type, plus sample handlers
        // showing off the attribute-handler extension points ([Header] sections, [Range] sliders).
        private PropertyGridConfig PgConfig()
        {
            if (_pgConfig != null) return _pgConfig;
            var c = new PropertyGridConfig();
            BuiltInFieldDrawers.Register(c.Drawers);
            c.Handlers.Register<HeaderAttribute>(new HeaderHandler());
            c.Handlers.Register<RangeAttribute>(new RangeHandler());
            _pgConfig = c;
            return c;
        }

        // ── Sample inspector model (exercises the reflection PropertyGrid) ───────
        private enum CollisionMode { Discrete, Continuous, ContinuousDynamic }
        private enum BodyType { Dynamic, Kinematic, Static }

        [AttributeUsage(AttributeTargets.Field)]
        private sealed class HeaderAttribute : Attribute { public readonly string Text; public HeaderAttribute(string t) => Text = t; }

        [AttributeUsage(AttributeTargets.Field)]
        private sealed class RangeAttribute : Attribute { public readonly float Min, Max; public RangeAttribute(float min, float max) { Min = min; Max = max; } }

        [AttributeUsage(AttributeTargets.Method)]
        private sealed class ButtonAttribute : Attribute { public string Label { get; } public ButtonAttribute(string label) => Label = label; }

        private sealed class PhysicsMaterial
        {
            public float Bounciness = 0.2f;
            public Prowl.Vector.Color Tint = new(168 / 255f, 85 / 255f, 247 / 255f, 1f);
        }

        private sealed class RigidbodyModel
        {
            [Header("Body")] public float Mass = 72f;
            public float LinearDrag = 0.4f;
            public Float3 Center = new(0f, 0.5f, 0f);
            [Range(0f, 1f)] public float Friction = 0.35f;

            [Header("Collision")] public BodyType Type = BodyType.Dynamic;
            public CollisionMode Collision = CollisionMode.Continuous;
            public bool UseGravity = true;

            [Header("Material")] public PhysicsMaterial Material = new();

            [Header("Tags")] public List<string> Tags = new() { "Player", "Solid" };

            [Button("Reset Inertia")]
            public void ResetInertia() { Mass = 1f; LinearDrag = 0f; }
        }

        // Draws an uppercase section header above the field it's attached to, then lets the field draw.
        private sealed class HeaderHandler : AttributeHandler
        {
            public override bool OnBeforeDraw(Paper paper, string id, Attribute attr, System.Reflection.FieldInfo field, object target, int depth)
            {
                var h = (HeaderAttribute)attr;
                var theme = Origami.Current;
                var font = theme.SemiBold ?? theme.Font;
                if (font != null)
                    paper.Box($"{id}_hdr").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Margin(12, 12, 8, 5)
                        .Text(h.Text.ToUpperInvariant(), font).FontSize(theme.Metrics.FontSizeSmall - 2f)
                        .LetterSpacing(0.6f).TextColor(theme.Primary.C700).Alignment(TextAlignment.MiddleLeft)
                        .IsNotInteractable();
                return true;
            }
        }

        // Replaces a [Range] float's control with a slider, keeping the grid's label column.
        private sealed class RangeHandler : AttributeHandler
        {
            public override bool OnDraw(Paper paper, string id, string label, Attribute attr,
                System.Reflection.FieldInfo field, object target, Action<object?> onChange, int depth)
            {
                var r = (RangeAttribute)attr;
                float val = Convert.ToSingle(field.GetValue(target) ?? 0f);
                var theme = Origami.Current;
                var m = theme.Metrics;
                var font = theme.Font;

                using (paper.Row(id).Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(m.RowHeight)
                    .Padding(12, 12, 4, 4).RowBetween(8).Enter())
                {
                    if (font != null)
                        paper.Box($"{id}_l").Width(m.LabelWidth).Height(m.RowHeight)
                            .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                            .Text(PropertyGridRenderer.FormatFieldName(field.Name), font)
                            .TextColor(theme.Ink.C300).FontSize(m.FontSize - 1f)
                            .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

                    using (paper.Row($"{id}_c").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        Origami.Slider(paper, $"{id}_s", val, v => onChange(v), r.Min, r.Max).Height(26).Show();
                }
                return true;
            }
        }

        // ── Modal ────────────────────────────────────────────────────────────────
        private void ModalDemo(Paper P)
        {
            Action<Paper> ModalBody(string id, string text) => p => p.Box(id)
                .Width(P.Percent(100)).Height(P.Auto)
                .Text(text, _geist).FontSize(11.5f * Palette.TS).TextColor(Palette.TMid)
                .Alignment(TextAlignment.Left).Wrap(TextWrapMode.Wrap);

            using (P.Column("moddemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(12).Enter())
            {
                using (P.Row("modtr").Width(P.Percent(100)).Height(P.Auto).Enter())
                    Origami.Button(P, "modopen", "Open modal").Primary().LeadingIcon(IconDraw(SvgIcon.Expand, Palette.White))
                        .OnClick(() => Origami.Modal("Regenerate Planet")
                            .Icon(IconDraw(SvgIcon.Bolt, Palette.Acc300))
                            .Content(ModalBody("modbig_msg", "Rebuild the quadtree mesh using the current seed and atmosphere settings? This may take a few seconds."))
                            .Button("Cancel", () => Modal.Pop())
                            .PrimaryButton("Regenerate", () => Modal.Pop())
                            .Show()).Show();

                DemoLabel(P, "modl", "Anatomy");
                Origami.Modal("Delete asset?")
                    .Icon(IconDraw(SvgIcon.Warn, Palette.C(251, 191, 36)))
                    .Content(ModalBody("modmini_msg", "This will permanently remove \"PlanetGen_Mesh\". This cannot be undone."))
                    .Button("Cancel", () => { })
                    .DangerButton("Delete", () => { })
                    .ShowEmbedded(P, "modmini");
            }
        }

        // ── Context Menu ─────────────────────────────────────────────────────────
        private void ContextMenuDemo(Paper P)
        {
            ContextMenu.Preview(P, "ctxdemo", b => b
                .Header("PlanetGen_Mesh")
                .Item("Open", () => { }, iconDraw: IconDraw(SvgIcon.FolderOpen, Palette.TMid), shortcut: "Ctrl O")
                .Item("Duplicate", () => { }, iconDraw: IconDraw(SvgIcon.Layers, Palette.TMid), shortcut: "Ctrl D")
                .Item("Rename", () => { }, iconDraw: IconDraw(SvgIcon.Pencil, Palette.TMid), shortcut: "F2")
                .Separator()
                .Item("Copy Path", () => { }, iconDraw: IconDraw(SvgIcon.Link, Palette.TMid))
                .Item("Delete", () => { }, iconDraw: IconDraw(SvgIcon.Trash, Palette.C(251, 113, 133)), shortcut: "Del", danger: true));
        }

        // ── Progress Bar ─────────────────────────────────────────────────────────
        private void ProgressBarDemo(Paper P)
        {
            using (P.Column("pbdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(12).Enter())
            {
                using (P.Column("pb1c").Width(P.Percent(100)).Height(P.Auto).ColBetween(6).Enter())
                {
                    DemoLabel(P, "pbl1", "Determinate 68%");
                    Origami.ProgressBar(P, "pb1", 0.68f).Show();
                }
                using (P.Column("pb2c").Width(P.Percent(100)).Height(P.Auto).ColBetween(6).Enter())
                {
                    DemoLabel(P, "pbl2", "Striped / active");
                    Origami.ProgressBar(P, "pb2", 0.44f).Striped().Show();
                }
                using (P.Row("pb3r").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.ProgressBar(P, "pb3", 0.66f).Ring().Show();
                    P.Box("pb3g").Width(14);
                    P.Box("pb3t").Width(P.Auto).Height(P.Auto).Margin(0, 0, P.Stretch(), P.Stretch())
                        .Text("Baking... 66%", _mono).FontSize(12 * Palette.TS).TextColor(Palette.TMid).Alignment(TextAlignment.MiddleLeft);
                }
            }
        }

        // ── File Dialog (embedded inline browser + modal trigger) ────────────────
        private void FileDialogDemo(Paper P)
        {
            using (P.Column("fddwrap").Width(P.Percent(100)).Height(P.Auto).ColBetween(10).Enter())
            {
                using (P.Row("fdtr").Width(P.Percent(100)).Height(P.Auto).Enter())
                    Origami.Button(P, "fdopen", "Open dialog").Primary().LeadingIcon(IconDraw(SvgIcon.FolderOpen, Palette.White))
                        .OnClick(() => FileDialog.Open(FileDialogMode.Open, _ => { }, AppContext.BaseDirectory)).Show();

                FileDialog.DrawEmbedded(P, "fddemo", 640f, 340f, startPath: AppContext.BaseDirectory);
            }
        }

        // ── Date Picker (functional field + styled popup, supports ranges) ───────
        private void DatePickerDemo(Paper P)
        {
            using (P.Column("dpdemo").Width(P.Percent(100)).Height(P.Auto).ColBetween(10).Enter())
            {
                DemoLabel(P, "dpl1", "Field + popup");
                Origami.DatePicker(P, "dp1", _dpDate, v => _dpDate = v).DateOnly().Width(P.Stretch()).Show();
                DemoLabel(P, "dpl2", "Inline / embedded (range)");
                Origami.DatePicker(P, "dp2", _dpStart, v => _dpStart = v)
                    .Range(_dpEnd, v => _dpEnd = v).Inline(224f).Show();
            }
        }

        // ── Button Group ─────────────────────────────────────────────────────────
        private void ButtonGroupDemo(Paper P)
        {
            using (P.Column("bgdemo").Width(P.Percent(100)).Height(P.Auto).Enter())
            {
                DemoLabel(P, "bgl1", "Grouped");
                using (P.Row("bgr1").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 0, 12).Enter())
                    Origami.ButtonGroup(P, "bgJoined", _bgJoined, v => _bgJoined = v)
                        .Item("Scene").Item("Game").Item("Asset").Show();

                DemoLabel(P, "bgl2", "Segmented / icons");
                using (P.Row("bgr2").Width(P.Percent(100)).Height(P.Auto).Enter())
                    Origami.ButtonGroup(P, "bgSeg", _bgSeg, v => _bgSeg = v).Segmented()
                        .Item("", IconDraw(SvgIcon.List, Palette.TMid))
                        .Item("", IconDraw(SvgIcon.Grid3, Palette.TMid))
                        .Item("", IconDraw(SvgIcon.Layers, Palette.TMid)).Show();
            }
        }

        // ── Toggle ───────────────────────────────────────────────────────────────
        private void ToggleDemo(Paper P)
        {
            using (P.Column("tgdemo").Width(P.Percent(100)).Height(P.Auto).Enter())
            {
                DemoLabel(P, "tgl1", "Switch");
                using (P.Row("tgr1").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 10, 0).Enter())
                {
                    Origami.Switch(P, "tgA", _tgA, v => _tgA = v).Show();
                    P.Box("tgsp1").Width(16);
                    Origami.Switch(P, "tgB", _tgB, v => _tgB = v).Show();
                    P.Box("tgsp2").Width(16);
                    Origami.Switch(P, "tgD", false, _ => { }).Disabled().Show();
                }

                Origami.Switch(P, "tgCast", _tgCast, v => _tgCast = v).LabelLeft("Cast Shadows").Stretch().Show();
                Origami.Switch(P, "tgTrig", _tgTrig, v => _tgTrig = v).LabelLeft("Is Trigger").Stretch().Show();

                DemoLabel(P, "tgl2", "Checkbox / Radio");
                using (P.Row("tgr2").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Origami.Checkbox(P, "cbA", _cbA, v => _cbA = v).Show();
                    P.Box("cbsp1").Width(14);
                    Origami.Checkbox(P, "cbI", true, _ => { }).Indeterminate(_cbInd).Show();
                    P.Box("cbsp2").Width(20);
                    Origami.Radio(P, "rbA", _rbA, v => { _rbA = true; _rbB = false; }).Show();
                    P.Box("cbsp3").Width(14);
                    Origami.Radio(P, "rbB", _rbB, v => { _rbB = true; _rbA = false; }).Show();
                }
            }
        }

        // ── Tabs ─────────────────────────────────────────────────────────────────
        private void TabsDemo(Paper P)
        {
            using (P.Column("tabdemo").Width(P.Percent(100)).Height(P.Auto).Enter())
            {
                DemoLabel(P, "tal1", "Underline");
                Origami.Tabs(P, "tabU", _tabU, v => _tabU = v).Underline()
                    .Tab("Scene", IconDraw(SvgIcon.Cube, Palette.THi))
                    .Tab("Game", IconDraw(SvgIcon.Layers, Palette.THi))
                    .Tab("Assets", IconDraw(SvgIcon.Folder, Palette.THi), "12").Show();

                P.Box("tabgap").Width(P.Percent(100)).Height(14);
                DemoLabel(P, "tal2", "Pills");
                Origami.Tabs(P, "tabP", _tabP, v => _tabP = v).Pills()
                    .Tab("All").Tab("Meshes").Tab("Materials").Show();
            }
        }

        // ── Button widget showcase (everything ButtonBuilder can do) ──────────────
        private void ButtonDemo(Paper P)
        {
            using (P.Column("btndemo").Width(P.Percent(100)).Height(P.Auto).Enter())
            {
                DemoLabel(P, "bl1", "Variants");
                using (P.Row("br1").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 0, 12).Enter())
                {
                    Btn(P, "bpri", "Primary", b => b.Primary());
                    Btn(P, "bsec", "Secondary", b => b);
                    Btn(P, "bgho", "Ghost", b => b.Ghost());
                    Btn(P, "bdel", "Delete", b => b.Danger().Soft());
                }

                DemoLabel(P, "bl2", "With icons");
                using (P.Row("br2").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 0, 12).Enter())
                {
                    Btn(P, "bcre", "Create", b => b.Primary().LeadingIcon(IconDraw(SvgIcon.Plus, Palette.White)));
                    Btn(P, "bopn", "Open", b => b.LeadingIcon(IconDraw(SvgIcon.FolderOpen, Palette.THi)));
                    Btn(P, "bgear", "", b => b.IconOnly().LeadingIcon(IconDraw(SvgIcon.Gear, Palette.THi)));
                    Btn(P, "bdis", "Disabled", b => b.Primary().Disabled());
                }

                DemoLabel(P, "bl3", "Styles");
                using (P.Row("br3").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 0, 12).Enter())
                {
                    Btn(P, "sfil", "Filled", b => b.Primary().Filled());
                    Btn(P, "sout", "Outline", b => b.Primary().Outline());
                    Btn(P, "sgho", "Ghost", b => b.Primary().Ghost());
                    Btn(P, "ssof", "Soft", b => b.Primary().Soft());
                    Btn(P, "slnk", "Link", b => b.Primary().Link());
                }

                DemoLabel(P, "bl4", "Sizes");
                using (P.Row("br4").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 0, 12).Enter())
                {
                    Btn(P, "zsm", "Small", b => b.Primary().Small());
                    Btn(P, "zmd", "Medium", b => b.Primary().Medium());
                    Btn(P, "zlg", "Large", b => b.Primary().Large());
                }

                DemoLabel(P, "bl5", "States");
                using (P.Row("br5").Width(P.Percent(100)).Height(P.Auto).Margin(0, 0, 0, 12).Enter())
                {
                    Btn(P, "stld", "Loading", b => b.Primary().Loading());
                    Btn(P, "stsh", "Shadow", b => b.Shadow());
                    Btn(P, "stpu", "Pulse", b => b.Primary().Pulse());
                    Btn(P, "sttt", "Tooltip", b => b.Tooltip("Helpful hint"));
                }

                DemoLabel(P, "bl6", "Semantic");
                using (P.Row("br6").Width(P.Percent(100)).Height(P.Auto).Enter())
                {
                    Btn(P, "vsuc", "Success", b => b.Success());
                    Btn(P, "vwar", "Warning", b => b.Warning());
                    Btn(P, "vinf", "Info", b => b.Info());
                    Btn(P, "vsub", "Subtle", b => b.Subtle());
                }
            }
        }

        private static Action<Canvas, Rect> IconDraw(string path, Color color)
            => (vg, r) => SvgIcon.Draw(vg, path, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, color, 1.6f);

        private void Btn(Paper P, string id, string label, Func<ButtonBuilder, ButtonBuilder> cfg)
        {
            cfg(Origami.Button(P, id, label)).Show();
            P.Box(id + "_sp").Width(8).Height(1); // horizontal gap
        }

        private void DemoLabel(Paper P, string id, string text)
        {
            P.Box(id).Width(P.Auto).Height(P.Auto).Margin(0, 0, 0, 7)
                .Text(text.ToUpperInvariant(), _geistSemi).FontSize(9.5f * Palette.TS).LetterSpacing(0.6f)
                .TextColor(Palette.TLo).Alignment(TextAlignment.MiddleLeft);
        }

        private void Tag(Paper P, string id, string text, float leftM)
        {
            P.Box(id).Width(P.Auto).Height(P.Auto).Rounded(5).Margin(leftM, 0, P.Stretch(), P.Stretch()).Padding(6, 6, 2, 2)
                .BackgroundColor(Palette.GlassIn).BorderColor(Palette.BdSoft).BorderWidth(1)
                .Text(text, _mono).FontSize(9.5f * Palette.TS).TextColor(Palette.TLo).Alignment(TextAlignment.MiddleCenter);
        }

        private void Icon(Paper P, string id, string path, float size, Color color, float sw, float leftM)
        {
            using (P.Box(id).Size(size).Margin(leftM, 0, P.Stretch(), P.Stretch()).Enter())
                P.Draw((vg, rect) => SvgIcon.Draw(vg, path, (float)rect.Min.X, (float)rect.Min.Y, size, color, sw));
        }
    }
}
