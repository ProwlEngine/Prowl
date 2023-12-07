using Prowl.Runtime;

namespace Prowl.Editor
{

    public interface IProjectSetting
    {
        // Define the properties and methods for your project settings
    }

    public class ProjectSettings
    {
        private Dictionary<Type, IProjectSetting> settings = new ();

        internal IEnumerable<Type> GetRegisteredSettingTypes() => settings.Keys;

        public T GetSetting<T>() where T : IProjectSetting, new() => (T)GetSetting(typeof(T));

        public IProjectSetting GetSetting(Type settingType)
        {
            if (settings.TryGetValue(settingType, out var setting))
                return setting;
            else
            {
                var newSetting = Activator.CreateInstance(settingType) as IProjectSetting;
                settings[settingType] = newSetting;
                return newSetting;
            }
        }

        public void Save(string? path = null)
        {
            path ??= Path.Combine(Project.ProjectDirectory, "ProjectSettings.setting");

            // Convert Type keys to string representations
            var convertedSettings = settings.ToDictionary(entry => entry.Key.FullName!, entry => entry.Value!);

            // Serialize settings to File
            StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(convertedSettings), new FileInfo(path));
        }

        public static ProjectSettings Load()
        {
            string filePath = Path.Combine(Project.ProjectDirectory, "ProjectSettings.setting");

            if (File.Exists(filePath))
            {
                // Deserialize JSON to settings
                var loadedSettings = TagSerializer.Deserialize<Dictionary<string, IProjectSetting>>(StringTagConverter.ReadFromFile(new FileInfo(filePath)));

                // Remove any settings whos type cannot be inferred with Type.GetType
                var convertedSettings = loadedSettings.Where(x => Type.GetType(x.Key) != null).ToDictionary(x => Type.GetType(x.Key), x => x.Value);

                // Create a new ProjectSettings instance and set the loaded settings
                return new() { settings = convertedSettings };
            }
            else
            {
                // If the file doesn't exist, return a new instance of ProjectSettings
                var newSettings = new ProjectSettings();
                newSettings.Save();
                return newSettings;
            }
        }
    }
}
