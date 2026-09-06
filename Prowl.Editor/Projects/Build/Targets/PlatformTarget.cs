// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Editor.Build;

/// <summary>
/// Something a build can be produced for.
/// </summary>
/// <remarks>
/// Registered data rather than an enum. A console cannot appear in a public enum in this repository, and
/// a target is really a platform and an architecture together, which an enum of platform names cannot
/// express. <see cref="Id"/> is the one value that must never change: it keys the asset variant cache and
/// appears in the build manifest, so renaming one invalidates shipped output.
/// </remarks>
public sealed record PlatformTarget
{
    /// <summary> The unique identifier that keys the asset variant cache and appears in the build manifest. Must never change once shipped. </summary>
    public required string Id { get; init; }

    /// <summary> The human-readable name shown in the editor UI. </summary>
    public required string DisplayName { get; init; }

    /// <summary>Grouping only, for example "desktop" or "mobile". A string so a new family needs no engine change.</summary>
    public required string Family { get; init; }

    /// <summary>
    /// The .NET runtime identifiers this target builds for.
    /// </summary>
    /// <remarks>
    /// A list because several real targets are not one architecture. An Android App Bundle carries
    /// multiple ABIs in one artifact, and a macOS universal binary is built per architecture and merged.
    /// </remarks>
    public required IReadOnlyList<string> RuntimeIdentifiers { get; init; }

    /// <summary> The capabilities this target supports, such as texture formats, graphics APIs, and feature flags. </summary>
    public required TargetCapabilities Capabilities { get; init; }

    /// <summary>
    /// The platform name assembly definitions and plugins are filtered by, when the target maps to one.
    /// </summary>
    public string? AssemblyPlatform { get; init; }

    /// <summary>Preprocessor symbols always defined for this target.</summary>
    public IReadOnlyList<string> Defines { get; init; } = [];

    /// <summary> Returns the <see cref="Id"/> value. </summary>
    public override string ToString() => Id;
}

/// <summary>
/// Supplies targets. The hook a platform shipped elsewhere implements, so its target metadata reaches
/// the registry without anything about it entering this repository.
/// </summary>
public interface IBuildTargetProvider
{
    /// <summary> Returns all <see cref="PlatformTarget"/> instances this provider supplies. </summary>
    IEnumerable<PlatformTarget> GetTargets();
}
