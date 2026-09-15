// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Well-known shader pass tag names and values.
/// </summary>
public static class PassTags
{
    public const string RenderOrder = "RenderOrder";
    public const string LightMode = "LightMode";

    public const string Opaque = "Opaque";
    public const string Transparent = "Transparent";
    public const string UI = "UI";
    public const string ShadowCaster = "ShadowCaster";
}
