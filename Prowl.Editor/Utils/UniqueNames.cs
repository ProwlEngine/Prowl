// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Utils;

/// <summary>
/// Helpers for generating names that don't collide with an existing set.
/// All entry points share one core: a separator pair plus a "does this name already exist?"
/// predicate, so callers can plug in whatever scope they need (string set, GO siblings,
/// filesystem, an InputActionMap, ...) without writing the loop themselves.
/// </summary>
public static class UniqueNames
{
    /// <summary>
    /// Find a name that doesn't satisfy <paramref name="exists"/>, by appending
    /// <c>{openSeparator}{N}{closeSeparator}</c> for the smallest free N.
    /// </summary>
    /// <param name="desired">The name we'd like to use.</param>
    /// <param name="exists">Returns true if the candidate name is already taken.</param>
    /// <param name="openSeparator">String inserted between the base name and the number - e.g. <c>" ("</c> for <c>"Cube (1)"</c>, <c>"_"</c> for <c>"name_1"</c>, or <c>""</c> for <c>"Action1"</c>.</param>
    /// <param name="closeSeparator">String appended after the number - e.g. <c>")"</c> for the parens style, empty for the others.</param>
    /// <param name="stripExistingSuffix">When true, an existing trailing suffix matching this format is parsed off and its number used as the starting point. <c>"Cube (3)"</c> tries <c>"Cube (4)"</c> first instead of <c>"Cube (3) (1)"</c>.</param>
    /// <param name="startNumber">First number to try when no suffix is found / parsed. Defaults to 1.</param>
    public static string MakeUnique(
        string desired,
        Func<string, bool> exists,
        string openSeparator = " (",
        string closeSeparator = ")",
        bool stripExistingSuffix = true,
        int startNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(exists);

        string baseName = desired;
        int n = startNumber;

        if (stripExistingSuffix
            && (closeSeparator.Length == 0 || desired.EndsWith(closeSeparator, StringComparison.Ordinal)))
        {
            int openIdx = openSeparator.Length == 0 ? -1 : desired.LastIndexOf(openSeparator, StringComparison.Ordinal);
            int numStart = openIdx >= 0 ? openIdx + openSeparator.Length : -1;
            int numLen = openIdx >= 0 ? desired.Length - numStart - closeSeparator.Length : 0;
            if (numStart > 0 && numLen > 0)
            {
                string numStr = desired.Substring(numStart, numLen);
                if (int.TryParse(numStr, out int parsed) && parsed > 0)
                {
                    baseName = desired.Substring(0, openIdx);
                    n = parsed + 1;
                }
            }
        }

        if (!exists(desired)) return desired;
        if (baseName != desired && !exists(baseName)) return baseName;

        while (true)
        {
            string candidate = $"{baseName}{openSeparator}{n}{closeSeparator}";
            if (!exists(candidate)) return candidate;
            n++;
        }
    }

    /// <summary>
    /// Make a name unique against <paramref name="parent"/>'s children - or, when parent is null,
    /// against <paramref name="scene"/>'s root objects. Uses the <c>" (N)"</c> convention with
    /// strip-and-increment, so duplicating <c>"Cube (3)"</c> yields <c>"Cube (4)"</c>.
    /// </summary>
    public static string ForGameObjectSibling(string desired, GameObject? parent, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<GameObject> siblings = parent != null ? parent.Children : scene.RootObjects;
        foreach (var s in siblings)
            taken.Add(s.Name);

        return MakeUnique(desired, name => taken.Contains(name));
    }

    /// <summary>
    /// Find a free filename inside <paramref name="folder"/>. Considers both files and directories
    /// as collisions. Result is <c>baseName + ext</c> (or <c>baseName (N) + ext</c>); the extension
    /// must include the leading dot (or be empty for folders). Uses the <c>" (N)"</c> convention.
    /// </summary>
    public static string ForFile(string folder, string baseName, string ext)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(baseName);
        ext ??= string.Empty;

        bool Exists(string nameWithoutExt)
        {
            string full = Path.Combine(folder, nameWithoutExt + ext);
            return File.Exists(full) || Directory.Exists(full);
        }

        string unique = MakeUnique(baseName, Exists, stripExistingSuffix: false);
        return unique + ext;
    }
}
