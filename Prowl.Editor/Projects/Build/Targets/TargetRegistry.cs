// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor.Build;

/// <summary>
/// Every target this engine can build for, including any added by a plugin.
/// </summary>
/// <remarks>
/// Target metadata only. Which pipeline builds a target is a separate question, answered by scanning for
/// <see cref="BuildPipeline"/> subclasses, so a platform that ships a pipeline is discovered without
/// registering anything at all.
/// </remarks>
public sealed class TargetRegistry
{
    private readonly Dictionary<string, PlatformTarget> _targets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registry the editor and tooling share.</summary>
    public static TargetRegistry Shared { get; } = CreateWithBuiltIns();

    public static TargetRegistry CreateWithBuiltIns()
    {
        var registry = new TargetRegistry();
        foreach (var target in BuiltInTargets.All)
            registry.Register(target);
        return registry;
    }

    /// <summary>Adds a target, replacing any already registered under the same id.</summary>
    public void Register(PlatformTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Checked here rather than where a build indexes the list, so a provider that gets this wrong
        // learns at registration instead of throwing an index error out of the middle of a build.
        if (target.RuntimeIdentifiers.Count == 0)
            throw new ArgumentException($"Target '{target.Id}' names no runtime identifier.", nameof(target));

        _targets[target.Id] = target;
    }

    public void RegisterFrom(IBuildTargetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (var target in provider.GetTargets())
        {
            // One malformed target must not take the provider's other targets down with it.
            try { Register(target); }
            catch (ArgumentException e)
            {
                Runtime.Debug.LogWarning($"[Build] {provider.GetType().Name}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Drops everything and restores the built ins, for when the editor rebuilds its registries.
    /// </summary>
    /// <remarks>
    /// Targets come from scanned types like every other registry, so they have to be discarded on a
    /// reload for the same reason. Otherwise a provider deleted from user code keeps its target
    /// registered for the rest of the session.
    /// </remarks>
    public void ResetToBuiltIns()
    {
        _targets.Clear();
        foreach (var target in BuiltInTargets.All)
            Register(target);
    }

    public bool TryGet(string id, out PlatformTarget? target) => _targets.TryGetValue(id, out target);

    public PlatformTarget Get(string id)
        => TryGet(id, out var target) && target != null
            ? target
            : throw new KeyNotFoundException($"No build target is registered as '{id}'.");

    /// <summary>Ordered by id, so a menu built from this looks the same on every machine.</summary>
    public IReadOnlyList<PlatformTarget> All
        => _targets.Values.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<PlatformTarget> ByFamily(string family)
        => All.Where(t => string.Equals(t.Family, family, StringComparison.OrdinalIgnoreCase)).ToList();
}

/// <summary>The targets that ship with the engine.</summary>
public static class BuiltInTargets
{
    public const string DesktopFamily = "desktop";

    private static readonly TargetCapabilities s_desktop = new()
    {
        TextureFormats = [TextureFormats.Bc7, TextureFormats.Bc5, TextureFormats.Rgba8],
        GraphicsApis = [GraphicsApis.Vulkan, GraphicsApis.OpenGL],
        Flags = new HashSet<string> { TargetFlags.Jit, TargetFlags.HotReload, TargetFlags.Threads, TargetFlags.Filesystem },
        MaxTextureSize = 16384,
    };

    private static readonly TargetCapabilities s_macOS = s_desktop with
    {
        GraphicsApis = [GraphicsApis.Metal, GraphicsApis.OpenGL],
    };

    public static readonly PlatformTarget WindowsX64 = new()
    {
        Id = "windows-x64",
        DisplayName = "Windows (x64)",
        Family = DesktopFamily,
        RuntimeIdentifiers = ["win-x64"],
        Capabilities = s_desktop,
        AssemblyPlatform = Projects.Scripting.BuildPlatforms.Windows,
        Defines = ["PROWL_WINDOWS"],
    };

    public static readonly PlatformTarget WindowsArm64 = WindowsX64 with
    {
        Id = "windows-arm64",
        DisplayName = "Windows (arm64)",
        RuntimeIdentifiers = ["win-arm64"],
    };

    public static readonly PlatformTarget LinuxX64 = new()
    {
        Id = "linux-x64",
        DisplayName = "Linux (x64)",
        Family = DesktopFamily,
        RuntimeIdentifiers = ["linux-x64"],
        Capabilities = s_desktop,
        AssemblyPlatform = Projects.Scripting.BuildPlatforms.Linux,
        Defines = ["PROWL_LINUX"],
    };

    public static readonly PlatformTarget LinuxArm64 = LinuxX64 with
    {
        Id = "linux-arm64",
        DisplayName = "Linux (arm64)",
        RuntimeIdentifiers = ["linux-arm64"],
    };

    public static readonly PlatformTarget MacOSX64 = new()
    {
        Id = "macos-x64",
        DisplayName = "macOS (Intel)",
        Family = DesktopFamily,
        RuntimeIdentifiers = ["osx-x64"],
        Capabilities = s_macOS,
        AssemblyPlatform = Projects.Scripting.BuildPlatforms.MacOS,
        Defines = ["PROWL_DESKTOP"],
    };

    public static readonly PlatformTarget MacOSArm64 = MacOSX64 with
    {
        Id = "macos-arm64",
        DisplayName = "macOS (Apple Silicon)",
        RuntimeIdentifiers = ["osx-arm64"],
    };

    /// <summary>Built per architecture and merged, which is why a target has a list of identifiers.</summary>
    public static readonly PlatformTarget MacOSUniversal = MacOSX64 with
    {
        Id = "macos-universal",
        DisplayName = "macOS (Universal)",
        RuntimeIdentifiers = ["osx-x64", "osx-arm64"],
    };

    public static IEnumerable<PlatformTarget> All =>
    [
        WindowsX64, WindowsArm64,
        LinuxX64, LinuxArm64,
        MacOSX64, MacOSArm64, MacOSUniversal,
    ];
}
