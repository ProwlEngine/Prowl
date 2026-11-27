// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;


namespace Prowl.Editor.Build;

/// <summary> How the built desktop application window is presented on startup. </summary>
public enum DesktopWindowMode
{
    Windowed,
    Fullscreen,
}

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

    /// <summary> Whether to produce a self-contained deployment that includes the .NET runtime. </summary>
    public bool SelfContained = false;
    /// <summary> Whether to trim unused code from the published output. </summary>
    public bool PublishTrimmed = false;
    /// <summary> Whether to compile the game ahead of time into one native executable. Needs the platform's native toolchain and builds for the host OS only. </summary>
    public bool PublishAot = false;
    /// <summary> Whether to open a console window alongside the application. </summary>
    public bool ShowConsole = false;
    /// <summary> Whether the application starts in windowed or fullscreen mode. </summary>
    public DesktopWindowMode WindowMode = DesktopWindowMode.Windowed;
    /// <summary> Whether a windowed application starts maximized. </summary>
    public bool WindowMaximized = false;
    /// <summary> Default width of the application window in pixels. </summary>
    public int WindowWidth = 1280;
    /// <summary> Default height of the application window in pixels. </summary>
    public int WindowHeight = 720;

    /// <summary>
    /// The registered target this profile builds for.
    /// </summary>
    /// <remarks>
    /// The registry is what lets a target carry more than one runtime identifier and lets arm64 exist at
    /// all, so everything the build needs comes from here rather than from <see cref="Platform"/>.
    /// </remarks>
    public PlatformTarget Target => TargetRegistry.Shared.Get(TargetId);

    /// <summary> Gets the effective target identifier, falling back from SelectedTargetId to the legacy Platform value when no registered target is selected. </summary>
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

    /// <summary> Adds the target's custom preprocessor defines to the given list. </summary>
    public override void ModifyDefines(List<string> defines) => defines.AddRange(Target.Defines);

    /// <summary> Resets all profile fields to their default values. </summary>
    public override void ToDefault()
    {
        Platform = BuildTarget.Windows;
        SelfContained = false;
        PublishTrimmed = false;
        PublishAot = false;
        ShowConsole = false;
        WindowMode = DesktopWindowMode.Windowed;
        WindowMaximized = false;
        WindowWidth = 1280;
        WindowHeight = 720;
    }

    public override void ApplyOverrides(PlatformOverrides overrides)
    {
        if (overrides.TryGetTarget(BuiltInTargets.DesktopFamily, out PlatformTarget t))
            SelectTarget(t);

        SelfContained = overrides.GetArg("selfcontained", SelfContained);
        ShowConsole = overrides.GetArg("console", ShowConsole);
        PublishTrimmed = overrides.GetArg("trimmed", PublishTrimmed);
        PublishAot = overrides.GetArg("aot", PublishAot);
        WindowMode = overrides.GetArg("windowmode", WindowMode);
        WindowMaximized = overrides.GetArg("maximized", WindowMaximized);
        WindowWidth = overrides.GetArg("width", WindowWidth);
        WindowHeight = overrides.GetArg("height", WindowHeight);

        overrides.ThrowIfUnknown();
    }
}

/// <summary>Renders <see cref="DesktopBuildProfile"/>. Kept out of the profile so the profile stays data.</summary>
public sealed class DesktopBuildProfileDrawer : IBuildProfileDrawer
{
    public Type ProfileType => typeof(DesktopBuildProfile);

    /// <summary> Renders the editor GUI for configuring a DesktopBuildProfile. </summary>
    public void OnGUI(Paper paper, PlatformBuildProfile profile)
    {
        if (profile is not DesktopBuildProfile desktop) return;

        // Every registered desktop target, not the three value enum, which cannot name arm64 at all.
        IReadOnlyList<PlatformTarget> targets = TargetRegistry.Shared.ByFamily(BuiltInTargets.DesktopFamily);
        string[] names = [.. targets.Select(t => t.DisplayName)];
        int current = Math.Max(0, targets.ToList().FindIndex(t => t.Id == desktop.TargetId));

        EditorGUI.SettingsRow(paper, "bld_platform", "Platform", () =>
            Origami.Dropdown(paper, "bld_platform_v", current, v =>
            {
                Record("Change Build Platform");
                desktop.SelectTarget(targets[v]);
                EditorRegistries.SaveSettings();
            }, names).Show(), separator: false);

        EditorGUI.SettingsToggle(paper, "bld_selfcontained", "Self-Contained", desktop.SelfContained,
            v => { Record("Toggle Self-Contained"); desktop.SelfContained = v; EditorRegistries.SaveSettings(); }, separator: false);

        EditorGUI.SettingsToggle(paper, "bld_trimmed", "Publish Trimmed", desktop.PublishTrimmed,
            v => { Record("Toggle Publish Trimmed"); desktop.PublishTrimmed = v; EditorRegistries.SaveSettings(); }, separator: false);

        EditorGUI.SettingsToggle(paper, "bld_aot", "Native AOT", desktop.PublishAot,
            v => { Record("Toggle Native AOT"); desktop.PublishAot = v; EditorRegistries.SaveSettings(); }, separator: false);

        EditorGUI.SettingsToggle(paper, "bld_console", "Show Console", desktop.ShowConsole,
            v => { Record("Toggle Show Console"); desktop.ShowConsole = v; EditorRegistries.SaveSettings(); }, separator: false);

        EditorGUI.SectionHeader(paper, "bld_window_h", "Window");

        EditorGUI.SettingsRow(paper, "bld_winmode", "Mode", () =>
            Origami.EnumDropdown(paper, "bld_winmode_v", desktop.WindowMode,
                v => { Record("Change Window Mode"); desktop.WindowMode = v; EditorRegistries.SaveSettings(); }).Show(), separator: false);

        if (desktop.WindowMode == DesktopWindowMode.Windowed)
        {
            EditorGUI.SettingsToggle(paper, "bld_maximized", "Maximized", desktop.WindowMaximized,
                v => { Record("Toggle Window Maximized"); desktop.WindowMaximized = v; EditorRegistries.SaveSettings(); }, separator: false);

            EditorGUI.SettingsRow(paper, "bld_width", "Width", () =>
                Origami.NumericField<int>(paper, "bld_width_v", desktop.WindowWidth,
                    v => { Record("Change Window Width"); desktop.WindowWidth = Math.Max(320, v); })
                    .OnCommit(EditorRegistries.SaveSettings).Min(320).Show(), separator: false);

            EditorGUI.SettingsRow(paper, "bld_height", "Height", () =>
                Origami.NumericField<int>(paper, "bld_height_v", desktop.WindowHeight,
                    v => { Record("Change Window Height"); desktop.WindowHeight = Math.Max(240, v); })
                    .OnCommit(EditorRegistries.SaveSettings).Min(240).Show(), separator: false);
        }
    }

    // Found again through the build settings, which a settings reload refills with new profiles.
    private static void Record(string name)
    {
        Undo.RecordObject(() => EditorRegistries.GetSettings<BuildSettings>().GetOrCreateProfile<DesktopBuildProfile>(), UndoOwner.OfSettings(typeof(BuildSettings)), name);
    }
}
