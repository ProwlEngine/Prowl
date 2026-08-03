// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Editor.Build;

/// <summary>Capability flags the engine itself understands. A platform may declare others.</summary>
public static class TargetFlags
{
    /// <summary>A just in time compiler exists. False on consoles and on any ahead of time build.</summary>
    public const string Jit = "jit";

    /// <summary>Assemblies can be swapped while running.</summary>
    public const string HotReload = "hot-reload";

    public const string Threads = "threads";

    /// <summary>An ordinary read and write filesystem, which the web does not have.</summary>
    public const string Filesystem = "filesystem";
}

/// <summary>Texture format ids the engine ships with. Registered strings, so a platform can add its own.</summary>
public static class TextureFormats
{
    public const string Rgba8 = "rgba8";
    public const string Bc5 = "bc5";
    public const string Bc7 = "bc7";
    public const string Etc2 = "etc2";
    public const string Astc6x6 = "astc_6x6";
    public const string Astc4x4 = "astc_4x4";
}

/// <summary>Graphics API ids the engine ships with.</summary>
public static class GraphicsApis
{
    public const string Vulkan = "vulkan";
    public const string D3D12 = "d3d12";
    public const string Metal = "metal";
    public const string OpenGL = "opengl";
    public const string OpenGLES3 = "gles3";
    public const string WebGPU = "webgpu";
}

/// <summary>
/// What a target can actually do.
/// </summary>
/// <remarks>
/// <see cref="TextureFormats"/> is the part the build reads today: it is what
/// <see cref="AssetVariantResolver.SelectProcessor"/> matches a processor against, so it decides which
/// form of an asset ships. The rest is declared and not yet consumed. <see cref="GraphicsApis"/> is for
/// choosing shader permutations, which waits on a shader stage, and <see cref="Flags"/> is for failing
/// validation on the desktop before failing on the device.
/// <para>
/// Everything is a registered string rather than an enum so a platform shipped out of tree can declare a
/// format or a capability this engine has never heard of.
/// </para>
/// </remarks>
public sealed record TargetCapabilities
{
    /// <summary>Accepted formats, best first. Variant selection takes the first the importer can produce.</summary>
    public IReadOnlyList<string> TextureFormats { get; init; } = [];

    public IReadOnlyList<string> GraphicsApis { get; init; } = [];

    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();

    public int MaxTextureSize { get; init; } = 16384;

    public bool Has(string flag) => Flags.Contains(flag);
}
