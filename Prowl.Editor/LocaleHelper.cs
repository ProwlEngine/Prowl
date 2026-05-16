// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Rosetta;

namespace Prowl.Editor;

/// <summary>
/// Shared locale data for the language dropdown in Preferences and the Project Launcher.
/// </summary>
public static class LocaleHelper
{
    public static readonly string[] Codes = { "en", "de", "fr", "es", "ja", "zh", "ko", "pt", "ru", "it", "pl", "tr" };
    public static readonly string[] Names = { "English", "Deutsch", "Francais", "Espanol", "Japanese", "Chinese", "Korean", "Portugues", "Russian", "Italiano", "Polski", "Turkce" };

    public static int GetIndex(string locale)
    {
        for (int i = 0; i < Codes.Length; i++)
            if (Codes[i] == locale) return i;
        return 0;
    }

    public static void SetLocale(int index)
    {
        if (index < 0 || index >= Codes.Length) return;
        string code = Codes[index];
        Loc.SetLocale(code);
        EditorSettings.Instance.Locale = code;
        EditorSettings.Instance.Save();
    }
}
