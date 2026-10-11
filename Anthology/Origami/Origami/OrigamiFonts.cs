// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;
using System.Reflection;

using Prowl.Scribe;

namespace Prowl.OrigamiUI;

public static class OrigamiFonts
{
    private static readonly object s_lock = new();

    private static FontFile? s_regular, s_medium, s_semiBold, s_bold, s_mono, s_iconSolid, s_iconOutline;

    public static FontFile? Regular => Load(ref s_regular, "Geist-Regular.ttf");
    public static FontFile? Medium => Load(ref s_medium, "Geist-Medium.ttf");
    public static FontFile? SemiBold => Load(ref s_semiBold, "Geist-SemiBold.ttf");
    public static FontFile? Bold => Load(ref s_bold, "Geist-Bold.ttf");
    public static FontFile? Mono => Load(ref s_mono, "JetBrainsMono-Regular.ttf");
    public static FontFile? IconSolid => Load(ref s_iconSolid, "fa-solid-900.ttf");
    public static FontFile? IconOutline => Load(ref s_iconOutline, "fa-regular-400.ttf");

    private static FontFile? Load(ref FontFile? slot, string file)
    {
        if (slot != null) return slot;
        lock (s_lock)
        {
            if (slot != null) return slot;
            using Stream? stream = typeof(OrigamiFonts).Assembly.GetManifestResourceStream("Prowl.OrigamiUI.Resources." + file);
            if (stream == null) return null;
            slot = new FontFile(stream);
            return slot;
        }
    }
}
