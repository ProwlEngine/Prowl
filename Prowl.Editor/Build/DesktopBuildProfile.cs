// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Build;

/// <summary>
/// Desktop specific build profile. This is used for builds targeting Windows, MacOSX and Linux.
/// Other platforms should inherit from PlatformBuildProfile and implement their own profile details.
/// </summary>
public class DesktopBuildProfile : PlatformBuildProfile
{
    public Type pipelineType => typeof(DesktopBuildPipeline);

    public override Type GetPipelineType()
    {
        return pipelineType;
    }

    public BuildTarget Platform = BuildTarget.Windows;
    public bool SelfContained = false;
    public bool PublishTrimmed = false;
    public int WindowWidth = 1280;
    public int WindowHeight = 720;

    public string RuntimeIdentifier
    {
        get => Platform switch
        {
            BuildTarget.Linux => "linux-x64",
            BuildTarget.MacOS => "osx-x64",
            _ => "win-x64",
        };
    }

    public override void OnGUI(Paper paper)
    {
        InspectorRow.Draw(paper, "bld_platform", "Platform", () =>
            Origami.EnumDropdown(paper, "bld_platform_v", Platform,
                v => { Platform = v; ProjectSettingsRegistry.SaveAll(); }).Show());

        Origami.Checkbox(paper, "bld_selfcontained", SelfContained,
                v => { SelfContained = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Self-Contained").Show();

        Origami.Checkbox(paper, "bld_trimmed", PublishTrimmed,
                v => { PublishTrimmed = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Publish Trimmed").Show();

        paper.Box("bld_sp2").Height(8);
        Origami.Header(paper, "bld_window_h", "Window").Underline().Show();

        InspectorRow.Draw(paper, "bld_width", "Width", () =>
            Origami.NumericField<int>(paper, "bld_width_v", WindowWidth,
                v => { WindowWidth = Math.Max(320, v); ProjectSettingsRegistry.SaveAll(); })
                .Min(320).Show());

        InspectorRow.Draw(paper, "bld_height", "Height", () =>
            Origami.NumericField<int>(paper, "bld_height_v", WindowHeight,
                v => { WindowHeight = Math.Max(240, v); ProjectSettingsRegistry.SaveAll(); })
                .Min(240).Show());
    }

    public override void ModifyDefines(List<string> defines)
    {
        // Always include a platform define
        defines.Add(Platform switch
        {
            BuildTarget.Windows => "PROWL_WINDOWS",
            BuildTarget.Linux => "PROWL_LINUX",
            _ => "PROWL_DESKTOP",
        });
    }

    public override void ToDefault()
    {
        Platform = BuildTarget.Windows;
        SelfContained = true;
        WindowWidth = 1280;
        WindowHeight = 720;
    }
}
