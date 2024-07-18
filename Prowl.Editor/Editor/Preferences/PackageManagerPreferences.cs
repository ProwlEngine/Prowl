using Prowl.Runtime.Utils;

namespace Prowl.Editor.Preferences
{
    [FilePath("PackageManager.pref", FilePathAttribute.Location.EditorPreference)]
    public class PackageManagerPreferences : ScriptableSingleton<PackageManagerPreferences>
    {
        public struct PackageSource(string name, string source, bool isEnabled)
        {
            public string Name = name;
            public string Source = source;
            public bool IsEnabled = isEnabled;
        }

        public List<PackageSource> Sources = [new("Nuget", "https://api.nuget.org/v3/index.json", true)];

        public bool IncludePrerelease = true; // True for the time being untill we have a stable 1.0 release
    }
}
