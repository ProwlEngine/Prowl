// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Utils;

namespace Prowl.Editor.Preferences;

[FilePath("PackageManager.pref", FilePathAttribute.Location.EditorPreference)]
public class PackageManagerPreferences : ScriptableSingleton<PackageManagerPreferences>
{
    public struct PackageSource(string name, string source, bool isEnabled)
    {
        public readonly string Name = name;
        public readonly string Source = source;
        public readonly bool IsEnabled = isEnabled;
    }

    public readonly List<PackageSource> Sources = [new("Nuget", "https://api.nuget.org/v3/index.json", true)];

    public bool IncludePrerelease = true; // True for the time being untill we have a stable 1.0 release
}
