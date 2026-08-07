// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.GUI;
using Prowl.Editor.Projects.Scripting;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Editor.Projects.Settings;


namespace Prowl.Editor.Build;

/// <summary>
/// Desktop specific build profile. This is used for builds targeting Windows, MacOSX and Linux.
/// Other platforms should inherit from PlatformBuildProfile and implement their own profile details.
/// </summary>
public class DesktopBuildProfile : PlatformBuildProfile
{
    public Type pipelineType => typeof(DesktopBuildPipeline);

    public override Type GetPipelineType() => pipelineType;

    /// <summary>Kept for projects saved before targets became data. <see cref="TargetId"/> supersedes it.</summary>
    public BuildTarget Platform = BuildTarget.Windows;

    /// <summary>The registered target id, empty in a project saved before this field existed.</summary>
    public string SelectedTargetId = "";

    public bool SelfContained = false;
    public bool PublishTrimmed = false;
    public int WindowWidth = 1280;
    public int WindowHeight = 720;

    /// <summary>
    /// The registered target this profile builds for.
    /// </summary>
    /// <remarks>
    /// The registry is what lets a target carry more than one runtime identifier and lets arm64 exist at
    /// all, so everything the build needs comes from here rather than from <see cref="Platform"/>.
    /// </remarks>
    public PlatformTarget Target => TargetRegistry.Shared.Get(TargetId);

    public string TargetId
    {
        get
        {
            if (!string.IsNullOrEmpty(SelectedTargetId) && TargetRegistry.Shared.TryGet(SelectedTargetId, out _))
                return SelectedTargetId;

            return Platform switch
            {
                BuildTarget.Linux => BuiltInTargets.LinuxX64.Id,
                BuildTarget.MacOS => BuiltInTargets.MacOSX64.Id,
                _ => BuiltInTargets.WindowsX64.Id,
            };
        }
    }

    /// <summary>Selects a target and keeps <see cref="Platform"/> consistent for anything still reading it.</summary>
    public void SelectTarget(PlatformTarget target)
    {
        SelectedTargetId = target.Id;
        Platform =
            target.AssemblyPlatform == BuildPlatforms.Linux ? BuildTarget.Linux :
            target.AssemblyPlatform == BuildPlatforms.MacOS ? BuildTarget.MacOS :
            BuildTarget.Windows;
    }

    /// <summary>The first identifier of the target. Publishing a multi architecture target needs them all.</summary>
    public string RuntimeIdentifier => Target.RuntimeIdentifiers[0];

    public override void ModifyDefines(List<string> defines) => defines.AddRange(Target.Defines);

    public override void ToDefault()
    {
        Platform = BuildTarget.Windows;
        SelfContained = false;
        PublishTrimmed = false;
        WindowWidth = 1280;
        WindowHeight = 720;
    }
}

/// <summary>Renders <see cref="DesktopBuildProfile"/>. Kept out of the profile so the profile stays data.</summary>
public sealed class DesktopBuildProfileDrawer : IBuildProfileDrawer
{
    public Type ProfileType => typeof(DesktopBuildProfile);

    public void OnGUI(Paper paper, PlatformBuildProfile profile)
    {
        if (profile is not DesktopBuildProfile desktop) return;

        // Every registered desktop target, not the three value enum, which cannot name arm64 at all.
        var targets = TargetRegistry.Shared.ByFamily(BuiltInTargets.DesktopFamily);
        var names = targets.Select(t => t.DisplayName).ToArray();
        int current = Math.Max(0, targets.ToList().FindIndex(t => t.Id == desktop.TargetId));

        EditorGUI.SettingsRow(paper, "bld_platform", "Platform", () =>
            Origami.Dropdown(paper, "bld_platform_v", current, v =>
            {
                desktop.SelectTarget(targets[v]);
                EditorRegistries.SaveSettings();
            }, names).Show(), separator: false);

        EditorGUI.SettingsToggle(paper, "bld_selfcontained", "Self-Contained", desktop.SelfContained,
            v => { desktop.SelfContained = v; EditorRegistries.SaveSettings(); }, separator: false);

        EditorGUI.SettingsToggle(paper, "bld_trimmed", "Publish Trimmed", desktop.PublishTrimmed,
            v => { desktop.PublishTrimmed = v; EditorRegistries.SaveSettings(); }, separator: false);

        EditorGUI.SectionHeader(paper, "bld_window_h", "Window");

        EditorGUI.SettingsRow(paper, "bld_width", "Width", () =>
            Origami.NumericField<int>(paper, "bld_width_v", desktop.WindowWidth,
                v => { desktop.WindowWidth = Math.Max(320, v); EditorRegistries.SaveSettings(); })
                .Min(320).Show(), separator: false);

        EditorGUI.SettingsRow(paper, "bld_height", "Height", () =>
            Origami.NumericField<int>(paper, "bld_height_v", desktop.WindowHeight,
                v => { desktop.WindowHeight = Math.Max(240, v); EditorRegistries.SaveSettings(); })
                .Min(240).Show(), separator: false);
    }
}
