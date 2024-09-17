// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

public class ImporterAttribute : Attribute
{
    public string[] Extensions { get; private set; }
    public string FileIcon { get; private set; }
    public Type GeneralType { get; private set; }
    public ImporterAttribute(string fileIcon, Type generalType, params string[] extensions)
    {
        FileIcon = fileIcon;
        Extensions = extensions;
        GeneralType = generalType;
    }

    public static Dictionary<string, Type> extToGeneralType = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, Type> extToImporter = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, string> extToIcon = new(StringComparer.OrdinalIgnoreCase);

    [OnAssemblyLoad]
    public static void GenerateLookUp()
    {
        extToImporter.Clear();
        extToIcon.Clear();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        foreach (var type in assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<ImporterAttribute>();
            if (attribute == null) continue;

            foreach (var extRW in attribute.Extensions)
            {
                var ext = extRW.ToLower();
                // Make sure the Extension is formatted correctly '.png' 1 dot at start
                if (ext[0] != '.') ext = '.' + ext;
                // Check if has more then 1 '.'
                if (ext.Count(x => x == '.') > 1) throw new Exception($"Extension {ext} is formatted incorrectly on importer: {type.Name}");

                if (extToImporter.TryGetValue(ext, out var oldType))
                    Debug.LogError($"Asset Importer Overwritten. {ext} extension already in use by: {oldType.Name}, being overwritten by: {type.Name}");
                extToImporter[ext] = type;
                extToIcon[ext] = attribute.FileIcon;
                extToGeneralType[ext] = attribute.GeneralType;
            }
        }
    }

    [OnAssemblyUnload]
    public static void ClearLookUp()
    {
        extToImporter.Clear();
    }

    /// <param name="extension">Extension type, including the '.' so '.png'</param>
    /// <returns>The importer type for that Extension</returns>
    public static Type? GetImporter(string extension)
    {
        if (extToImporter.TryGetValue(extension, out var importerType))
            return importerType;
        return null;
    }

    public static Type? GetGeneralType(string extension)
    {
        if (extToGeneralType.TryGetValue(extension, out var type))
            return type;
        return null;
    }

    public static bool SupportsExtension(string extension)
    {
        return extToImporter.ContainsKey(extension);
    }

    public static string GetIconForExtension(string extension)
    {
        if (extToIcon.TryGetValue(extension, out var fileIcon))
            return fileIcon;
        return "FileIcon.png";
    }
}

public class CustomEditorAttribute : Attribute
{
    public Type Type { get; private set; }

    public CustomEditorAttribute(Type type)
    {
        Type = type;
    }

    public static Dictionary<Type, Type> typeToEditor = new();

    [OnAssemblyLoad]
    public static void GenerateLookUp()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        foreach (var type in assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<CustomEditorAttribute>();
            if (attribute == null) continue;

            if (typeToEditor.TryGetValue(attribute.Type, out var oldType))
                Debug.LogError($"Custom Editor Overwritten. {attribute.Type.Name} already has a custom Editor: {oldType.Name}, being overwritten by: {type.Name}");
            typeToEditor[attribute.Type] = type;
        }
    }

    [OnAssemblyUnload]
    public static void ClearLookUp()
    {
        typeToEditor.Clear();
    }

    /// <returns>The editor type for that Extension</returns>
    public static Type? GetEditor(Type type)
    {
        if (typeToEditor.TryGetValue(type, out var editorType))
            return editorType;
        // If no direct custom editor, look for a base class custom editor
        foreach (var pair in typeToEditor)
            if (pair.Key.IsAssignableFrom(type))
                return pair.Value;
        return null;
    }
}
