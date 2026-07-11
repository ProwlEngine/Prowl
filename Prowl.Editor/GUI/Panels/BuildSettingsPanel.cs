// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Prowl.Editor.Build;
using Prowl.Editor.GUI.SceneView;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Scribe;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Theming;

using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

public class BuildSettingsPanel : DockPanel
{

    public static BuildSettingsPanel Instance { get; set; }

    public class BuildStatusReport
    {
        public enum BuildStatusReportType
        {
            Info,
            Progress,
        }

        public LogSeverity Severity;
        public BuildStatusReportType Type;
        public string Message;
        public float Progress; // Only used for Progress type
    }

    public struct BuildPipelineInfo
    {
        public string Name;
        public string Icon;
        public Type BuildPipelineType;
    }

    public static string BuildState = "";

    public static float BuildProgress = 0f;

    public static bool IsBuildRunning => _activeProgress != null && !_activeProgress.IsComplete;

    public override string Title => Loc.Get("panel.build");

    public override string Icon => EditorIcons.Hammer;

    private BuildSettings _buildSettings;

    private static BuildProgress _activeProgress;

    private int _selectedIndex;

    private List<BuildPipelineInfo> _buildPlatforms;

    private static UnitValue ST => UnitValue.StretchOne;

    private const float HeaderH = 44f;
    private const float FooterH = 56f;
    private const float PlatformW = 400f;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        _buildSettings ??= ProjectSettingsRegistry.Get<BuildSettings>();
        _buildPlatforms ??= GetBuildPlatforms();
        Instance ??= this;

        if (_activeProgress != null)
        {
            BuildProgress = _activeProgress.ProgressValue;
            var state = _activeProgress.GetState();
            if (state != null)
            {
                BuildState = state.Message.Contains("\n") ? state.Message.Split('\n')[0] : state.Message;
                BuildState = BuildState.Trim();
            }

            // Build finished: drop the reference so the footer returns to the idle summary
            // instead of being stuck on the last progress line forever, and so IsBuildRunning
            // (and thus a second Build click) sees the build as no longer running.
            if (_activeProgress.IsComplete)
                _activeProgress = null;
        }
        else
        {
            BuildProgress = 0f;
            BuildState = Loc.Get("editor.status_ready");
        }

        float bodyH = height - FooterH;
        float scenesW = Math.Max(width - PlatformW - 1, 200);

        using (paper.Column("bp_root").Size(width, height).Clip().Enter())
        {
            using (paper.Row("bp_body").Width(width).Height(bodyH).Clip().Enter())
            {
                DrawScenesColumn(paper, font, scenesW, bodyH);
                paper.Box("bp_vdiv").Width(1).Height(ST).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
                DrawPlatformColumn(paper, font, PlatformW, bodyH);
            }

            DrawFooter(paper, font, width);
        }
    }

    // ---------------------------------------------------------------------
    //  LEFT COLUMN Scenes in Build
    // ---------------------------------------------------------------------

    private void DrawScenesColumn(Paper paper, FontFile font, float colW, float bodyH)
    {
        using (paper.Column("bp_scenes").Width(colW).Height(bodyH).Enter())
        {
            HeaderStrip(paper, "bp_scenes_h", Loc.Get("build.scenes_header"), font, () =>
                ChipButton(paper, "bp_scenes_add", $"{EditorIcons.Plus}  {Loc.Get("build.add_open")}", AddOpenScene));

            Origami.ScrollView(paper, "bp_scenes_list", colW, bodyH - HeaderH - 1)
                .Padding(8, 8, 8, 8)
                .Body(() =>
                {
                    var scenes = _buildSettings.Scenes;
                    if (scenes.Count == 0)
                    {
                        paper.Box("bp_scenes_empty").Width(ST).Height(40).Margin(6, 6, 6, 0).IsNotInteractable()
                            .Text(Loc.Get("build.no_scenes"), font)
                            .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleCenter);
                        return;
                    }

                    // Container element that holds the drag state + per-row layout positions.
                    var listEl = paper.CurrentParent;

                    int buildIndex = 0;
                    for (int i = 0; i < scenes.Count; i++)
                        DrawSceneRow(paper, font, scenes, i, scenes[i].Enabled ? buildIndex++ : -1, listEl);
                });
        }
    }

    private void DrawSceneRow(Paper paper, FontFile font, List<SceneBuildEntry> scenes, int i, int buildIndex, ElementHandle listEl)
    {
        int idx = i;
        var scene = scenes[i];
        bool on = scene.Enabled;
        var mono = EditorTheme.FontMono ?? font;

        // The scene GUID is a stable per-row key so Paper element identity survives reorders.
        string sk = scene.SceneGuid.ToString();
        bool beingDragged = paper.GetElementStorage<string>(listEl, "dragSk", null!) == sk;

        string displayName = !string.IsNullOrEmpty(scene.Path)
            ? System.IO.Path.GetFileNameWithoutExtension(scene.Path)
            : sk[..8];

        const float rowH = 28f;

        var rowB = paper.Row($"bp_sc_{sk}").Width(ST).Height(rowH).Rounded(7).Margin(0, 0, 0, 3)
            .Padding(8, 4, 0, 0).RowBetween(8)
            .BackgroundColor(beingDragged || on ? EditorTheme.Selected : Color.Transparent)
            .Hovered.BackgroundColor(on ? EditorTheme.Selected : EditorTheme.Hover).End();

        // Record this row's centre-Y each frame so the drag can pick the nearest drop target.
        rowB.OnPostLayout(idx, (rowIndex, _, r) =>
        {
            var cys = paper.GetElementStorage<List<float>>(listEl, "rowCys", null!);
            if (cys == null) { cys = new List<float>(); paper.SetElementStorage(listEl, "rowCys", cys); }
            while (cys.Count <= rowIndex) cys.Add(0f);
            cys[rowIndex] = (float)(r.Min.Y + r.Size.Y * 0.5f);
        });

        using (rowB.Enter())
        {
            var grip = paper.Box($"bp_sc_{sk}_grip").Width(12).Height(ST)
                .Text(EditorIcons.Grip, font).TextColor(beingDragged ? EditorTheme.Ink500 : EditorTheme.Ink300)
                .Hovered.TextColor(EditorTheme.Ink500).End()
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

            grip.OnDragStart(sk, (k, _) => paper.SetElementStorage(listEl, "dragSk", k))
                .OnDragging(sk, (k, _) =>
                {
                    int cur = scenes.FindIndex(s => s.SceneGuid.ToString() == k);
                    var cys = paper.GetElementStorage<List<float>>(listEl, "rowCys", null!);
                    if (cur < 0 || cys == null) return;

                    float py = (float)paper.PointerPos.Y;
                    int target = cur; float best = float.MaxValue;
                    for (int t = 0; t < cys.Count && t < scenes.Count; t++)
                    {
                        float d = MathF.Abs(cys[t] - py);
                        if (d < best) { best = d; target = t; }
                    }
                    MoveScene(scenes, cur, target);
                })
                .OnDragEnd(_ =>
                {
                    paper.SetElementStorage(listEl, "dragSk", (string?)null);
                    ProjectSettingsRegistry.SaveAll();
                });

            paper.Box($"bp_sc_{sk}_idx").Width(20).Height(ST).IsNotInteractable()
                .Text(buildIndex >= 0 ? buildIndex.ToString() : "-", mono)
                .TextColor(on ? EditorTheme.Ink400 : EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

            using (paper.Row($"bp_sc_{sk}_sww").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(4, 4, ST, ST).Enter())
                Origami.Switch(paper, $"bp_sc_{sk}_sw", scene.Enabled, v =>
                {
                    scene.Enabled = v;
                    ProjectSettingsRegistry.SaveAll();
                }).NoLabel().Show();

            paper.Box($"bp_sc_{sk}_nm").Width(ST).Height(ST).IsNotInteractable()
                .Text(displayName, font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            paper.Box($"bp_sc_{sk}_rm").Width(rowH).Height(ST).Rounded(6)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink300)
                .Hovered.TextColor(EditorTheme.Red400).End()
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .OnClick(idx, (ci, _) =>
                {
                    scenes.RemoveAt(ci);
                    ProjectSettingsRegistry.SaveAll();
                });
        }
    }

    // Reorder in-memory only; the drag-end handler persists once so a drag doesn't thrash the disk.
    private static void MoveScene(List<SceneBuildEntry> scenes, int from, int to)
    {
        if (from == to || from < 0 || from >= scenes.Count || to < 0 || to >= scenes.Count) return;
        var v = scenes[from];
        scenes.RemoveAt(from);
        scenes.Insert(to, v);
    }

    private void AddOpenScene()
    {
        var db = EditorAssetDatabase.Instance;
        var entry = EditorSceneManager.CurrentScenePath != null ? db?.GetEntry(EditorSceneManager.CurrentScenePath) : null;
        if (entry == null)
        {
            Toasts.Warning(Loc.Get("build.toast_not_saved"), Loc.Get("build.toast_not_saved_msg"));
            return;
        }

        if (_buildSettings.Scenes.Any(s => s.SceneGuid == entry.Guid))
        {
            Toasts.Warning(Loc.Get("build.toast_already"), Loc.Get("build.toast_already_msg"));
            return;
        }

        _buildSettings.Scenes.Add(new SceneBuildEntry { Path = entry.Path, SceneGuid = entry.Guid });
        ProjectSettingsRegistry.SaveAll();
    }

    private void TryStartBuild(bool andRun)
    {
        if (IsBuildRunning)
        {
            Toasts.Warning(Loc.Get("build.toast_build_running"), Loc.Get("build.toast_build_running_msg"));
            return;
        }

        if (_buildSettings.Scenes.Count(s => s.Enabled) == 0)
        {
            Toasts.Warning(Loc.Get("build.toast_no_scenes"), Loc.Get("build.toast_no_scenes_msg"));
            return;
        }

        _activeProgress = ProjectBuilder.StartBuildAsync(andRun, _buildSettings.OutputDirectory);
    }

    // ---------------------------------------------------------------------
    //  RIGHT COLUMN Platform + build configuration
    // ---------------------------------------------------------------------

    private void DrawPlatformColumn(Paper paper, FontFile font, float colW, float bodyH)
    {
        using (paper.Column("bp_plat").Width(colW).Height(bodyH).Enter())
        {
            HeaderStrip(paper, "bp_plat_h", Loc.Get("build.platform"), font, null);

            Origami.ScrollView(paper, "bp_plat_scroll", colW, bodyH - HeaderH - 1).Body(() =>
            {
                DrawPlatformGrid(paper, font);

                // Per-platform profile fields (preserved from original behaviour).
                if (_selectedIndex >= 0 && _selectedIndex < _buildPlatforms.Count)
                {
                    var profile = _buildSettings.GetOrCreateProfile(_buildPlatforms[_selectedIndex].BuildPipelineType);
                    if (profile != null)
                    {
                        EditorGUI.SectionHeader(paper, "bp_prof_h", _buildPlatforms[_selectedIndex].Name, first: true);
                        profile.OnGUI(paper);
                    }
                }

                EditorGUI.SectionHeader(paper, "bp_cfg_h", Loc.Get("build.configuration"));

                EditorGUI.SettingsRow(paper, "bp_cfg_config", Loc.Get("build.configuration"), () =>
                    Origami.EnumDropdown(paper, "bp_cfg_config_v", _buildSettings.Config,
                        v => { _buildSettings.Config = v; ProjectSettingsRegistry.SaveAll(); }).Show(), separator: false);

                EditorGUI.SettingsRow(paper, "bp_cfg_out", Loc.Get("build.output_dir"), () =>
                    Origami.TextField(paper, "bp_cfg_out_v", _buildSettings.OutputDirectory,
                        v => { _buildSettings.OutputDirectory = v; ProjectSettingsRegistry.SaveAll(); }).Show(), separator: false);

                EditorGUI.SettingsRow(paper, "bp_cfg_pkg", Loc.Get("build.asset_packaging"), () =>
                    Origami.EnumDropdown(paper, "bp_cfg_pkg_v", _buildSettings.PackagingMode,
                        v => { _buildSettings.PackagingMode = v; ProjectSettingsRegistry.SaveAll(); }).Show(), separator: false);

                EditorGUI.SettingsRow(paper, "bp_cfg_asset", Loc.Get("build.asset_export"), () =>
                    Origami.EnumDropdown(paper, "bp_cfg_asset_v", _buildSettings.AssetMode,
                        v => { _buildSettings.AssetMode = v; ProjectSettingsRegistry.SaveAll(); }).Show(), separator: false);

                if (_buildSettings.PackagingMode == AssetPackagingMode.ProwlPak)
                    EditorGUI.SettingsRow(paper, "bp_cfg_pak", Loc.Get("build.max_pak_size"), () =>
                        Origami.IntSlider(paper, "bp_cfg_pak_v", _buildSettings.MaxPakSizeMB,
                            v => { _buildSettings.MaxPakSizeMB = v; ProjectSettingsRegistry.SaveAll(); },
                            256, 4096).Show(), separator: false);

                paper.Box("bp_plat_pad").Height(12);
            });
        }
    }

    private const float CardGap = 6f;

    private void DrawPlatformGrid(Paper paper, FontFile font)
    {
        var slots = new List<(string name, string icon, int realIndex)>();
        for (int i = 0; i < _buildPlatforms.Count; i++)
            slots.Add((_buildPlatforms[i].Name, _buildPlatforms[i].Icon, i));
        slots.Add(("Android", EditorIcons.Mobile, -1));
        while (slots.Count < 4 || slots.Count % 2 != 0)
            slots.Add((null, null, -1));

        using (paper.Column("bp_grid").Width(ST).Height(UnitValue.Auto)
            .Padding(10, 10, 10, 10).ColBetween(CardGap).Enter())
        {
            for (int r = 0; r < slots.Count; r += 2)
                using (paper.Row($"bp_grow_{r}").Width(ST).Height(UnitValue.Auto).RowBetween(CardGap).Enter())
                {
                    DrawPlatformCard(paper, font, r, slots[r]);
                    DrawPlatformCard(paper, font, r + 1, slots[r + 1]);
                }
        }
    }

    private void DrawPlatformCard(Paper paper, FontFile font, int cell, (string name, string icon, int realIndex) slot)
    {
        bool empty = slot.name == null;
        bool selectable = slot.realIndex >= 0;
        bool sel = selectable && _selectedIndex == slot.realIndex;

        var card = paper.Column($"bp_card_{cell}").Width(ST).MinHeight(78)
            .Rounded(9).Padding(6, 6, 11, 11).ColBetween(6)
            .BackgroundColor(sel ? EditorTheme.Selected : EditorTheme.Glass)
            .BorderColor(sel ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(1);

        if (selectable)
            card.Hovered.BorderColor(sel ? EditorTheme.Accent : EditorTheme.BorderStrong).End()
                .OnClick(slot.realIndex, (id, _) => _selectedIndex = id);
        else
            card.IsNotInteractable();

        using (card.Enter())
        {
            if (empty) return;

            // Selectable = normal ink; Android placeholder = dimmed (coming soon).
            Color fg = selectable ? (sel ? EditorTheme.Ink500 : EditorTheme.Ink400) : EditorTheme.Ink200;

            using (paper.Column($"bp_card_{cell}_c").Width(ST).Height(UnitValue.Auto)
                .Margin(0, 0, ST, ST).ColBetween(6).Enter())
            {
                paper.Box($"bp_card_{cell}_ic").Width(ST).Height(20).IsNotInteractable()
                    .Text(slot.icon, font).TextColor(fg)
                    .FontSize(EditorTheme.FontSizeLarge).Alignment(TextAlignment.MiddleCenter);

                paper.Box($"bp_card_{cell}_l").Width(ST).Height(15).IsNotInteractable()
                    .Text(slot.name, font).TextColor(fg)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter).TextTruncate();
            }
        }
    }

    // ---------------------------------------------------------------------
    //  FOOTER summary + build actions / progress
    // ---------------------------------------------------------------------

    private void DrawFooter(Paper paper, FontFile font, float width)
    {
        paper.Box("bp_ftr_top").Width(width).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

        var mono = EditorTheme.FontMono ?? font;
        bool running = IsBuildRunning;

        int enabled = _buildSettings.Scenes.Count(s => s.Enabled);
        string platName = _selectedIndex >= 0 && _selectedIndex < _buildPlatforms.Count
            ? _buildPlatforms[_selectedIndex].Name : "-";
        string leftText = running
            ? BuildState
            : Loc.Get("build.scene_count", new { count = enabled }) + $"  ·  {platName}  ·  {_buildSettings.Config}";

        using (paper.Row("bp_footer").Width(width).Height(FooterH - 1).Padding(16, 16, 0, 0).Enter())
        {
            paper.Box("bp_ftr_ic").Width(20).Height(ST).Margin(0, 0, ST, ST).IsNotInteractable()
                .Text(EditorIcons.Hammer, font)
                .TextColor(running ? EditorTheme.Accent : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);

            paper.Box("bp_ftr_sum").Width(running ? UnitValue.Pixels(180) : UnitValue.Auto).Height(ST)
                .Margin(10, 0, ST, ST).IsNotInteractable()
                .Text(leftText, mono).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            // Progress bar fills the middle, between the summary and the action buttons.
            using (paper.Row("bp_ftr_progw").Width(ST).Height(UnitValue.Auto).Margin(14, 14, ST, ST).Enter())
                Origami.ProgressBar(paper, "bp_ftr_bar", BuildProgress).Thickness(8).ShowPercent("F0").Show();

            ChipButton(paper, "bp_ftr_build", $"{EditorIcons.Hammer}  {Loc.Get("build.build")}", () => TryStartBuild(false));

            paper.Box("bp_ftr_bgap").Width(8).Height(1).Margin(0, 0, ST, ST).IsNotInteractable();

            CtaButton(paper, "bp_ftr_buildrun", $"{EditorIcons.Play}  {Loc.Get("build.build_run")}", EditorTheme.Accent, () => TryStartBuild(true));
        }
    }

    // ---------------------------------------------------------------------
    //  Shared chrome helpers
    // ---------------------------------------------------------------------

    private static void HeaderStrip(Paper paper, string id, string title, FontFile font, Action drawAction)
    {
        var semi = EditorTheme.FontSemiBold ?? font;

        using (paper.Row(id).Width(ST).Height(HeaderH).Padding(14, 14, 0, 0).RowBetween(8).Enter())
        {
            paper.Box($"{id}_t").Width(ST).Height(ST).Margin(0, 0, ST, ST).IsNotInteractable()
                .Text(title.ToUpperInvariant(), semi).TextColor(EditorTheme.AccentText)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            drawAction?.Invoke();
        }
        paper.Box($"{id}_d").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
    }

    private static void ChipButton(Paper paper, string id, string label, Action onClick)
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).Width(UnitValue.Auto).Height(28).Margin(0, 0, ST, ST).Rounded(8).Padding(12, 12, 0, 0)
            .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(EditorTheme.BorderStrong).End()
            .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    private static void CtaButton(Paper paper, string id, string label, Color bg, Action onClick)
    {
        var font = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        paper.Box(id).Width(UnitValue.Auto).Height(28).Margin(0, 0, ST, ST).Rounded(8).Padding(16, 16, 0, 0)
            .BackgroundColor(bg)
            .Hovered.BackgroundColor(Color.FromArgb(230, bg.R, bg.G, bg.B)).End()
            .Text(label, font).TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    public List<BuildPipelineInfo> GetBuildPlatforms()
    {
        List<BuildPipelineInfo> pipelines = new List<BuildPipelineInfo>();

        foreach (var type in ScriptAssemblyManager.GetAllTypes())
        {
            if (!type.IsSubclassOf(typeof(BuildPipeline)) || type.IsAbstract)
                continue;

            string name = type.Name;
            string icon = EditorIcons.Desktop;
            try
            {
                if (Activator.CreateInstance(type) is BuildPipeline bp)
                {
                    name = bp.DisplayName;
                    icon = bp.Icon;
                }
            }
            catch { }

            pipelines.Add(new BuildPipelineInfo { BuildPipelineType = type, Name = name, Icon = icon });
        }

        return pipelines;
    }
}
