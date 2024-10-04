// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Runtime;
using Prowl.Editor.Assets;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;

namespace Prowl.Editor;

public class ScriptedEditor
{
    private static readonly Dictionary<Type, Type> s_editorLookup = [];

    public Gui gui => Gui.ActiveGUI;

    public object target { get; private set; }

    public virtual void OnEnable() { }

    public virtual void OnInspectorGUI() => DrawDefaultInspector();

    public virtual void OnDisable() { }

    public void DrawDefaultInspector()
    {
        // PropertyGrid would fall apart if this was a value type, but ScriptedEditors don't work on value types!
        object refTarget = target;
        if (EditorGUI.PropertyGrid("Default Drawer", ref refTarget, EditorGUI.TargetFields.Serializable, EditorGUI.PropertyGridConfig.NoHeader))
        {
            MethodInfo? method = target.GetType().GetMethod("OnValidate", BindingFlags.Public | BindingFlags.Instance);
            method?.Invoke(refTarget, null);
        }
    }


    [OnAssemblyLoad]
    public static void GenerateLookUp()
    {
        List<Type> typesToSearch = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).ToList();
        List<CustomEditorAttribute> customEditors = [];

        // Register all editors directly referencing types
        foreach (Type type in typesToSearch)
        {
            CustomEditorAttribute? attribute = type.GetCustomAttribute<CustomEditorAttribute>();

            if (attribute == null)
                continue;

            if (!type.IsAssignableTo(typeof(ScriptedEditor)))
            {
                Debug.LogWarning($"{nameof(CustomEditorAttribute)} is not valid on type '{type}'. Attribute is only valid on types inheriting from {nameof(ScriptedEditor)}. Attribute will be ignored.");
                continue;
            }

            if (s_editorLookup.TryGetValue(attribute.Type, out Type? oldType))
                Debug.LogError($"Custom Editor Overwritten. {attribute.Type.Name} already has a custom Editor: {oldType.Name}, being overwritten by: {type.Name}");

            s_editorLookup[attribute.Type] = type;
            customEditors.Add(attribute);
        }

        // Register all editors indirectly referencing types via inheritance
        // i.e : Editor for type A will be applied to type B which inherits A
        foreach (Type type in typesToSearch)
        {
            Type? baseType = type;
            Type? editor = null;

            // Walk up the inheritance tree until an editor that supports inheritance is found
            while (baseType.BaseType != null)
            {
                baseType = baseType.BaseType;

                if (!s_editorLookup.TryGetValue(baseType, out editor))
                    continue;

                // Does this editor apply only for the given type or for every other?
                if (!editor.GetCustomAttribute<CustomEditorAttribute>()!.Inheritable)
                {
                    editor = null;
                    continue;
                }

                break;
            }

            if (editor != null)
                s_editorLookup[type] = editor;
        }
    }


    [OnAssemblyUnload]
    public static void ClearLookUp()
    {
        s_editorLookup.Clear();
    }


    /// <summary>
    /// Gets the first matching <see cref="ScriptedEditor"/> type that has a <see cref="CustomEditorAttribute"/> for the provided type.
    /// </summary>
    /// <param name="type">The <see cref="Type"/> to get a <see cref="ScriptedEditor"/> type for.</param>
    /// <returns>A <see cref="ScriptedEditor"/> type for the given <see cref="Type"/>, or null if none exists.</returns>
    public static Type? GetEditorType(Type type, bool allowDefaultDrawer = true)
    {
        if (s_editorLookup.TryGetValue(type, out Type? editorType))
            return editorType;


        // No dedicated editor found, so return the default
        if (allowDefaultDrawer && type.IsClass)
            return typeof(ScriptedEditor);

        return null;
    }


    public static ScriptedEditor? CreateEditor(object target, Type type, bool allowDefaultDrawer = true)
    {
        Type? editorType = GetEditorType(type, allowDefaultDrawer);

        if (editorType != null)
        {
            ScriptedEditor? customEditor = Activator.CreateInstance(editorType) as ScriptedEditor;

            if (customEditor != null)
            {
                customEditor.target = target;
                customEditor.OnEnable();
                return customEditor;
            }
        }

        return null;
    }


    public static ScriptedEditor? CreateEditor(object target, bool allowDefaultDrawer = true)
    {
        return CreateEditor(target, target.GetType(), allowDefaultDrawer);
    }
}
