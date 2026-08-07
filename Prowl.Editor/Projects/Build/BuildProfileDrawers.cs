// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;

namespace Prowl.Editor.Build;

/// <summary>
/// Draws the settings for one kind of <see cref="PlatformBuildProfile"/>. Profiles are data and live in
/// this namespace as plain data, so the drawing lives beside it rather than on it.
/// </summary>
public interface IBuildProfileDrawer
{
    Type ProfileType { get; }
    void OnGUI(Paper paper, PlatformBuildProfile profile);
}

/// <summary>Maps a profile type to the drawer that renders it.</summary>
public static class BuildProfileDrawers
{
    private static readonly Dictionary<Type, IBuildProfileDrawer> s_drawers = new();

    static BuildProfileDrawers()
    {
        Register(new DesktopBuildProfileDrawer());
    }

    public static void Register(IBuildProfileDrawer drawer)
        => s_drawers[drawer.ProfileType] = drawer;

    /// <summary>Draws the profile, walking up its base types so a subclass falls back to its parent's drawer.</summary>
    public static void Draw(Paper paper, PlatformBuildProfile profile)
    {
        for (var type = profile.GetType(); type != null; type = type.BaseType)
        {
            if (!s_drawers.TryGetValue(type, out var drawer)) continue;
            drawer.OnGUI(paper, profile);
            return;
        }
    }
}
