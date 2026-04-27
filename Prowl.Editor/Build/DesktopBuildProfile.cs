// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Widgets;
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
        EditorGUI.EnumDropdown(paper, "bld_platform", "Platform", Platform)
            .OnValueChanged(v => { Platform = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.Toggle(paper, "bld_selfcontained", "Self-Contained", SelfContained)
            .OnValueChanged(v => { SelfContained = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.Toggle(paper, "bld_trimmed", "Publish Trimmed", PublishTrimmed)
            .OnValueChanged(v => { PublishTrimmed = v; ProjectSettingsRegistry.SaveAll(); });

        paper.Box("bld_sp2").Height(8);
        EditorGUI.Header(paper, "bld_window_h", "Window");
        EditorGUI.Separator(paper, "bld_window_sep");

        EditorGUI.IntField(paper, "bld_width", WindowWidth, "Width")
            .OnValueChanged(v => { WindowWidth = Math.Max(320, v); ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.IntField(paper, "bld_height", WindowHeight, "Height")
            .OnValueChanged(v => { WindowHeight = Math.Max(240, v); ProjectSettingsRegistry.SaveAll(); });
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
