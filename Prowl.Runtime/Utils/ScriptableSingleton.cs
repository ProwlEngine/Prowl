using System;
using System.IO;

namespace Prowl.Runtime.Utils
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FilePathAttribute(string filePath, FilePathAttribute.Location fileLocation) : Attribute
    {
        public enum Location
        {
            Data,
            Setting,
            EditorPreference
        }

        public string FilePath { get; } = filePath;
        public Location FileLocation { get; } = fileLocation;
    }

    public abstract class ScriptableSingleton<T> where T : ScriptableSingleton<T>, new()
    {
        private static T? instance;

        public static T Instance => instance ??= LoadOrCreateInstance();

        public void Save()
        {
            StringTagConverter.WriteToFile(Serializer.Serialize(this), new(GetFilePath()));
        }

        protected string GetFilePath()
        {
            if(Application.DataPath == null)
                throw new InvalidOperationException("Application.DataPath is null, ensure Application.Run() has been called, and a DataPath has been assigned!");

            if (Attribute.GetCustomAttribute(GetType(), typeof(FilePathAttribute)) is FilePathAttribute attribute)
            {
                string directory = string.Empty;
                switch (attribute.FileLocation)
                {
                    case FilePathAttribute.Location.Data:
                        directory = Application.DataPath;
                        break;
                    case FilePathAttribute.Location.Setting:
                        directory = Path.Combine(Application.DataPath, "ProjectSettings");
                        break;
                    case FilePathAttribute.Location.EditorPreference:
                        // Persistent across all projects
                        if (Application.isEditor == false)
                            throw new InvalidOperationException("Preferences are only available in the editor");
                        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prowl", "Editor");
                        break;
                }
                // Ensure Directory Exists
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, attribute.FilePath);
            }
            return string.Empty;
        }

        private static T LoadOrCreateInstance()
        {
            string filePath = new T().GetFilePath();

            if (File.Exists(filePath))
            {
                return Serializer.Deserialize<T>(StringTagConverter.ReadFromFile(new FileInfo(filePath)))!;
            }
            else
            {
                var newInstance = new T();
                newInstance.Save();
                return newInstance;
            }
        }
    }
}
