// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Drawing;

using Prowl.Editor.Build;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Theming;


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
        public Type BuildPipelineType;
    }

    public static string BuildState = "";

    public static System.Threading.Thread BuildPipelineThread;

    public static float BuildProgress = 0f;

    public static bool IsBuildRunning =>
       BuildPipelineThread != null && BuildPipelineThread.IsAlive;

    public override string Title => Loc.Get("panel.build");

    public override string Icon => EditorIcons.Hammer;

    private BuildSettings _buildSettings;

    private BuildProgress _activeProgress;

    private int _selectedIndex;

    private List<BuildPipelineInfo> _buildPlatforms;

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
        }
        else
        {
            BuildProgress = 0f;
            BuildState = "Ready";
        }

        // The height of the build settings

        var buildBarHeight = Math.Max(height * 0.2f, 150);

        var bsHeight = height - buildBarHeight;

        using (paper
            .Column("bp_root")
            .Size(width, height)
            .Enter())
        {

            using (paper.Row("bp_settings").Size(width, bsHeight).Enter())
            {
                // Left sidebar - category list
                float sidebarW = 220f;
                using (paper.Column("bp_sidebar")
                    .Padding(new UnitValue(EditorTheme.SidePixelPadding))
                    .Width(sidebarW).Height(bsHeight)
                    .BackgroundColor(EditorTheme.Neutral200)
                    .Clip()
                    .Enter())
                {
                    paper.Box("bp_sidebar_header")
                        .Height(28).ChildLeft(8)
                        .Text("Platforms", font).TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                    Origami.Separator(paper, "bp_sidebar_sep").Show();

                    for (int i = 0; i < _buildPlatforms.Count; i++)
                    {
                        int idx = i;
                        var entry = _buildPlatforms[i];

                        bool isSelected = _selectedIndex == i;
                        //string icon = string.IsNullOrEmpty(entry.Icon) ? EditorIcons.Gear : entry.Icon;
                        string icon = EditorIcons.Gear;

                        paper.Box($"bp_cat_{i}")
                            .Height(30).ChildLeft(8).Rounded(3)
                            .Margin(0, 0, 0, EditorTheme.VerticalNavbarSpacing)
                            .BackgroundColor(isSelected ? EditorTheme.Purple400 : Color.Transparent)
                            .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                            .Text($" {icon}  {entry.Name}", font)
                            .TextColor(isSelected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSize - 1)
                            .Alignment(TextAlignment.MiddleLeft)
                            .OnClick(idx, (id, _) => _selectedIndex = id);
                    }
                }

                // Separator
                paper.Box("bp_divider").Width(1).Height(bsHeight).BackgroundColor(EditorTheme.Ink200);

                // Right content - selected settings
                float contentW = width - sidebarW - 1;
                Origami.ScrollView(paper, "bp_content", contentW, bsHeight)
                    .Padding(EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding)
                    .Body(() =>
                {
                    paper.Box("bp_content_pad").Height(8);
                    _buildSettings.OnGUI(paper, contentW - 16);
                    if (_buildPlatforms.Count >= _selectedIndex && _selectedIndex >= 0)
                    {
                        var profile = _buildSettings.GetOrCreateProfile(_buildPlatforms[_selectedIndex].BuildPipelineType);
                        if (profile != null)
                        {
                            profile.OnGUI(paper);
                        }
                    }
                    paper.Box("bp_content_pad2").Height(16);
                });
            }

            paper.Box("bp_divider").Width(width).Height(1).BackgroundColor(EditorTheme.Ink200);

            using (paper
                .Column("bp_buildmenu")
                .Margin(24, 24)
                .Enter())
            {
                Origami.ProgressBar(paper, "bp_progressBar", BuildProgress).Thickness(10).Show();


                paper.Box("bp_sidebar_header")
                    .Height(28).ChildLeft(8)
                    .Clip()
                    .Text(BuildState, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);


                // Build buttons
                using (paper.Row("bp_bld_buttons").Height(32).RowBetween(8).ChildLeft(4).Enter())
                {
                    paper.Box("bp_spacer")
                        .Width(UnitValue.StretchOne);

                    Origami.Button(paper, "bld_build", $"{EditorIcons.Hammer}  Build", () =>
                    {
                        _activeProgress = ProjectBuilder.StartBuildAsync(false, _buildSettings.OutputDirectory);
                    }).Width(120).Show();

                    Origami.Button(paper, "bld_buildrun", $"{EditorIcons.Play}  Build & Run", () =>
                    {
                        _activeProgress = ProjectBuilder.StartBuildAsync(true, _buildSettings.OutputDirectory);
                    }).Width(140).Show();
                }
            }

        }
    }


    public List<BuildPipelineInfo> GetBuildPlatforms()
    {
        List<BuildPipelineInfo> pipelines = new List<BuildPipelineInfo>();

        foreach (var type in ScriptAssemblyManager.GetAllTypes())
        {
            if (type.IsSubclassOf(typeof(BuildPipeline)))
            {
                pipelines.Add(new BuildPipelineInfo()
                {
                    BuildPipelineType = type,
                    Name = type.Name
                });
            }
        }

        return pipelines;
    }
}
