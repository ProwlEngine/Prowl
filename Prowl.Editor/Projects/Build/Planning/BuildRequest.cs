// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;

namespace Prowl.Editor.Build;

/// <summary>
/// Everything a build needs, as plain data.
/// </summary>
/// <remarks>
/// One place the stages read from, instead of each reaching into ambient state. What is left over is
/// the genuinely editor owned pieces, which a desktop build still holds in instance fields because the
/// script compiler and the settings exporter need the live project.
/// </remarks>
public sealed record BuildRequest
{
    /// <summary> Name of the project to build. </summary>
    public required string ProjectName { get; init; }
    /// <summary> Root directory of the project on disk. </summary>
    public required string ProjectRoot { get; init; }

    /// <summary>Where imported assets already sit on disk, one file per GUID.</summary>
    public required string AssetCachePath { get; init; }

    /// <summary>Scratch space the build may create and delete freely.</summary>
    public required string TempPath { get; init; }

    /// <summary> Directory where the finished build is written. </summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Scenes to ship, in order. The first is the one the player opens.</summary>
    public required IReadOnlyList<Guid> Scenes { get; init; }

    /// <summary> Build configuration, Debug or Release. </summary>
    public required BuildConfiguration Configuration { get; init; }
    /// <summary> How assets are packaged: Embedded, ProwlPak, or LooseFiles. </summary>
    public required AssetPackagingMode Packaging { get; init; }
    /// <summary> When true, only assets that are dependencies of the shipped scenes are included. </summary>
    public required bool DependenciesOnly { get; init; }

    /// <summary> Maximum size of each asset pack in megabytes. Defaults to 512. </summary>
    public int MaxPackSizeMB { get; init; } = 512;

    /// <summary> Per-platform build profile with scripting define symbols and other platform-specific settings. Null for default behaviour. </summary>
    public PlatformBuildProfile? Profile { get; init; }

    /// <summary> The first scene in the Scenes list, or Guid.Empty if the list is empty. </summary>
    public Guid DefaultScene => Scenes.Count > 0 ? Scenes[0] : Guid.Empty;
}
