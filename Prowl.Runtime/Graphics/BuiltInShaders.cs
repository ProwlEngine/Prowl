// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;

using GTexture = Prowl.Graphite.Texture;

namespace Prowl.Runtime;

/// <summary>
/// Prowl's shader library, the includes its shaders and user shaders share as <c>Prowl/Name.glsl</c>, and the named
/// default textures its Properties blocks use beyond Graphite's own (white, black, gray, normal).
/// </summary>
internal static class BuiltInShaders
{
    public const string Prefix = "Prowl/";

    private static readonly List<GTexture> s_defaults = [];

    /// <summary>The source of a Prowl include, null for any other name.</summary>
    public static string? Resolve(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        string path = $"Assets/Defaults/Shaders/{name[Prefix.Length..]}";
        return EmbeddedResources.Exists(path) ? EmbeddedResources.ReadAllText(path) : null;
    }

    public static void RegisterDefaults(Renderer renderer)
    {
        Register(renderer, "surface", 0, 255, 0, 255);
        Register(renderer, "emission", 0, 0, 0, 255);
        Register(renderer, "gray18", 45, 45, 45, 255);
    }

    private static void Register(Renderer renderer, string name, byte r, byte g, byte b, byte a)
    {
        GTexture texture = renderer.Device.ResourceFactory.CreateTexture(Graphite.TextureDescription.Texture2D(1, 1, 1, 1, Graphite.PixelFormat.R8_G8_B8_A8_UNorm, Graphite.TextureUsage.Sampled));
        texture.Name = $"Prowl default {name}";
        renderer.Device.UpdateTexture<byte>(texture, [r, g, b, a]);
        renderer.Textures.RegisterDefault(name, texture);
        s_defaults.Add(texture);
    }

    public static void Release()
    {
        foreach (GTexture texture in s_defaults) texture.Dispose();
        s_defaults.Clear();
    }
}
