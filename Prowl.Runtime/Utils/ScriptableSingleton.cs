// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

namespace Prowl.Runtime.Utils;

[AttributeUsage(AttributeTargets.Class)]
public class FilePathAttribute(string filePath, FilePathAttribute.Location fileLocation) : Attribute
{
    public enum Location
    {
        /// <summary>
        /// Data that is saved to the Application.DataPath
        /// Good for when you want to extend the path and save to like Application.DataPath/Library/Temp/MyData
        /// </summary>
        Data,
        /// <summary>
        /// Settings that are saved to the Application.DataPath/ProjectSettings
        /// Intended for settings that are saved to the project and will be built with the project
        /// </summary>
        Setting,
        /// <summary>
        /// Editor Settings that are saved to the Application.DataPath/ProjectSettings/Editor
        /// Intended for settings that are editor only and per project, they will not be built with the project
        /// </summary>
        EditorSetting,
        /// <summary>
        /// Editor Preferences that are saved to the Environment.SpecialFolder.ApplicationData/Prowl/Editor
        /// Intended for settings that are editor only and are shared across all projects/editors
        /// </summary>
        EditorPreference
    }

    public string FilePath { get; } = filePath;
    public Location FileLocation { get; } = fileLocation;
}

public abstract class ScriptableSingleton<T> where T : ScriptableSingleton<T>, new()
{
    protected static T? _instance;

    public static T Instance => _instance ??= LoadOrCreateInstance();

    public virtual void OnValidate() { }

    public void Save()
    {
        StringTagConverter.WriteToFile(Serializer.Serialize(this), new(GetFilePath(Application.DataPath)));
    }

    protected string GetFilePath(string? dataPath)
    {

        if (Attribute.GetCustomAttribute(GetType(), typeof(FilePathAttribute)) is FilePathAttribute attribute)
        {
            string directory = string.Empty;
            switch (attribute.FileLocation)
            {
                case FilePathAttribute.Location.Data:
                    ArgumentNullException.ThrowIfNull(dataPath);

                    directory = dataPath;
                    break;
                case FilePathAttribute.Location.Setting:
                    ArgumentNullException.ThrowIfNull(dataPath);

                    directory = Path.Combine(dataPath, "ProjectSettings");
                    break;
                case FilePathAttribute.Location.EditorSetting:
                    ArgumentNullException.ThrowIfNull(dataPath);

                    // Persistent across sessions for a single project
                    if (Application.IsEditor == false)
                        throw new InvalidOperationException("Editor Settings are only available in the editor");
                    directory = Path.Combine(dataPath, "ProjectSettings", "Editor");
                    break;
                case FilePathAttribute.Location.EditorPreference:
                    // Persistent across all projects
                    // TODO: !Application.isRunning is just a hack to allow CLI operations that do not depend on the editor
                    if (!Application.IsRunning || Application.IsEditor == false)
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
        string filePath = new T().GetFilePath(Application.DataPath);

        if (File.Exists(filePath))
        {
            try
            {
                var deserialized = Serializer.Deserialize<T>(StringTagConverter.ReadFromFile(new FileInfo(filePath)))!;
                deserialized.OnValidate();
                if (deserialized != null)
                    return deserialized;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load {typeof(T).Name}, Replacing Corrupted file. At {filePath}: {e.Message}");
            }
        }

        var newInstance = new T();
        newInstance.OnValidate();
        newInstance.Save();
        return newInstance;
    }

    static void CopyTo(string? dataPath)
    {
        if (dataPath == Application.DataPath)
            throw new InvalidOperationException("Cannot copy to the same directory");

        string cur = Instance.GetFilePath(Application.DataPath);
        string dest = Instance.GetFilePath(dataPath);

        if (File.Exists(cur))
            File.Copy(cur, dest, true);
    }
}
