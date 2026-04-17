// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

using Prowl.Editor.Build;
using Prowl.Editor.Docking;
using Prowl.Editor.Scripting;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Silk.NET.Vulkan;


namespace Prowl.Editor.Panels;

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

    public override string Title => "Build Project";

    public override string Icon => EditorIcons.Hammer;

    private BuildSettings _buildSettings;

    private int _selectedIndex;

    private List<BuildPipelineInfo> _buildPlatforms;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        _buildSettings ??= ProjectSettingsRegistry.Get<BuildSettings>();
        _buildPlatforms ??= GetBuildPlatforms();
        Instance ??= this;


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
                // Left sidebar — category list
                float sidebarW = 220f;
                using (paper.Column("bp_sidebar")
                    .Border(new UnitValue(Units.Pixels, EditorTheme.SidePixelPadding))
                    .Width(sidebarW).Height(bsHeight)
                    .BackgroundColor(EditorTheme.Neutral200)
                    .Clip()
                    .Enter())
                {
                    paper.Box("bp_sidebar_header")
                        .Height(28).ChildLeft(8)
                        .Text("Platforms", font).TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                    EditorGUI.Separator(paper, "bp_sidebar_sep");

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

                // Right content — selected settings
                float contentW = width - sidebarW - 1;
                using (ScrollView.Begin(paper, "bp_content", contentW, bsHeight,
                    EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding))
                {
                    paper.Box("bp_content_pad").Height(8);
                    _buildSettings.OnGUI(paper, contentW - 16);
                    //entries[_selectedIndex].Instance.OnGUI(paper, contentW - 16);
                    paper.Box("bp_content_pad2").Height(16);
                }
            }

            paper.Box("bp_divider").Width(width).Height(1).BackgroundColor(EditorTheme.Ink200);

            using (paper
                .Column("bp_buildmenu")
                .Margin(24,24)
                .Enter())
            {
                EditorGUI.ProgressBar(paper, "bp_progressBar", "", BuildProgress, 10);


                paper.Box("bp_sidebar_header")
                    .Height(28).ChildLeft(8)
                    .Text(BuildState, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);


                // Build buttons
                using (paper.Row("bp_bld_buttons").Height(32).RowBetween(8).ChildLeft(4).Enter())
                {
                    paper.Box("bp_spacer")
                        .Width(UnitValue.StretchOne);

                    EditorGUI.Button(paper, "bld_build", $"{EditorIcons.Hammer}  Build", width: 120)
                        .OnValueChanged(_ => BuildSettings.StartBuildProcess(false));

                    EditorGUI.Button(paper, "bld_buildrun", $"{EditorIcons.Play}  Build & Run", width: 140);
                        //.OnValueChanged(_ => BuildSettings.StartBuildProcess(true));
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
