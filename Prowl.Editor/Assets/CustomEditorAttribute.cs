// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

[AttributeUsage(AttributeTargets.Class)]
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
                if (type != null)
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
