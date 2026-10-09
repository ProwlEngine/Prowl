// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>Which flavour of OpenGL the engine creates its context for and writes its shaders against.</summary>
public enum GraphicsTarget
{
    /// <summary>Desktop OpenGL core. The newest version the driver offers, except macOS which stops at 4.1.</summary>
    OpenGL,

    /// <summary>OpenGL ES 3.2, the API mobile devices run.</summary>
    OpenGLES,
}

/// <summary>Features beyond the baseline every target has. Query them with <see cref="GraphicsCapabilities.Has"/>.</summary>
public enum GraphicsFeature
{
    /// <summary>Compute shaders and dispatch.</summary>
    ComputeShaders,

    /// <summary>Shader storage buffers (SSBOs).</summary>
    StorageBuffers,

    /// <summary>Storage buffers read from vertex shaders, which the spec lets a driver offer none of.</summary>
    VertexStorageBuffers,

    /// <summary>Storage buffers read from fragment shaders, which the spec lets a driver offer none of.</summary>
    FragmentStorageBuffers,

    /// <summary>Image load and store from shaders.</summary>
    ImageLoadStore,

    /// <summary><c>binding =</c> layout qualifiers on uniform blocks, samplers and buffers in GLSL.</summary>
    ShaderBindingLayout,

    /// <summary>Several index ranges drawn in one call.</summary>
    MultiDraw,

    /// <summary>Clamping depth instead of clipping at the near and far planes.</summary>
    DepthClamp,

    /// <summary>Linear filtering of 32 bit float textures.</summary>
    FloatLinearFiltering,

    /// <summary>Reading a texture straight back to the CPU without a framebuffer.</summary>
    TextureReadback,

    /// <summary>Driver debug messages and debug groups.</summary>
    DebugOutput,
}

/// <summary>
/// What the current graphics target allows on this device. A feature is available only when the target includes it
/// and the driver provides it, so code written against it runs the same on every machine that shares the target.
/// </summary>
public sealed class GraphicsCapabilities
{
    private readonly HashSet<GraphicsFeature> _features = new();
    private readonly HashSet<string> _extensions;

    public GraphicsTarget Target { get; }
    public int MajorVersion { get; }
    public int MinorVersion { get; }
    public bool IsES => Target == GraphicsTarget.OpenGLES;

    /// <summary>The version number GLSL source is written for, such as 410, 460 or 320.</summary>
    public int ShaderVersion => MajorVersion * 100 + MinorVersion * 10;

    /// <summary>Storage blocks a vertex or fragment shader needs for the engine to keep its data in buffers there.</summary>
    internal const int StageStorageBlocksNeeded = 4;

    internal GraphicsCapabilities(GraphicsTarget target, int major, int minor, HashSet<string> extensions,
        int vertexStorageBlocks = 16, int fragmentStorageBlocks = 16)
    {
        Target = target;
        MajorVersion = major;
        MinorVersion = minor;
        _extensions = extensions;

        if (target == GraphicsTarget.OpenGLES)
        {
            bool es31 = AtLeast(3, 1);
            Set(GraphicsFeature.ComputeShaders, es31);
            Set(GraphicsFeature.StorageBuffers, es31);
            Set(GraphicsFeature.ImageLoadStore, es31);
            Set(GraphicsFeature.ShaderBindingLayout, es31);
            Set(GraphicsFeature.DepthClamp, HasExtension("GL_EXT_depth_clamp"));
            Set(GraphicsFeature.FloatLinearFiltering, HasExtension("GL_OES_texture_float_linear"));
            Set(GraphicsFeature.DebugOutput, AtLeast(3, 2));
        }
        else
        {
            bool gl43 = AtLeast(4, 3);
            Set(GraphicsFeature.ComputeShaders, gl43);
            Set(GraphicsFeature.StorageBuffers, gl43);
            Set(GraphicsFeature.ImageLoadStore, AtLeast(4, 2));
            Set(GraphicsFeature.ShaderBindingLayout, AtLeast(4, 2));
            Set(GraphicsFeature.MultiDraw, true);
            Set(GraphicsFeature.DepthClamp, true);
            Set(GraphicsFeature.FloatLinearFiltering, true);
            Set(GraphicsFeature.TextureReadback, true);
            Set(GraphicsFeature.DebugOutput, gl43);
        }

        bool storage = Has(GraphicsFeature.StorageBuffers);
        Set(GraphicsFeature.VertexStorageBuffers, storage && vertexStorageBlocks >= StageStorageBlocksNeeded);
        Set(GraphicsFeature.FragmentStorageBuffers, storage && fragmentStorageBlocks >= StageStorageBlocksNeeded);
    }

    /// <summary>Whether the target allows <paramref name="feature"/> and this device provides it.</summary>
    public bool Has(GraphicsFeature feature) => _features.Contains(feature);

    /// <summary>Whether the driver reports an extension, by its full name such as <c>GL_EXT_depth_clamp</c>.</summary>
    public bool HasExtension(string name) => _extensions.Contains(name);

    public bool AtLeast(int major, int minor) => MajorVersion > major || (MajorVersion == major && MinorVersion >= minor);

    private void Set(GraphicsFeature feature, bool available)
    {
        if (available) _features.Add(feature);
    }

    /// <summary>
    /// Checks a feature something is about to rely on. When it is missing this warns once per caller, naming what
    /// loses out, so the caller can take its fallback.
    /// </summary>
    public bool Require(GraphicsFeature feature, string usedBy)
    {
        if (Has(feature)) return true;
        Debug.LogWarningOnce($"GraphicsFeature.{feature}.{usedBy}",
            $"{usedBy} needs {feature}, which {Describe()} does not support, so it falls back or is turned off.");
        return false;
    }

    /// <summary>The target and version, such as "OpenGL 4.1" or "OpenGL ES 3.2".</summary>
    public string Describe() => $"{(IsES ? "OpenGL ES" : "OpenGL")} {MajorVersion}.{MinorVersion}";

    public override string ToString()
    {
        var features = new List<string>();
        foreach (GraphicsFeature feature in Enum.GetValues<GraphicsFeature>())
            if (Has(feature)) features.Add(feature.ToString());
        return $"{Describe()} with {string.Join(", ", features)}";
    }
}
