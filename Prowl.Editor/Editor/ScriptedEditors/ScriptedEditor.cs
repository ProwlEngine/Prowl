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
    public Gui gui => Gui.ActiveGUI;

    public object target { get; internal set; }

    public virtual void OnEnable() { }

    public virtual void OnInspectorGUI()
    {
        object t = target;

        if (EditorGUI.PropertyGrid("Default Drawer", ref t, EditorGUI.TargetFields.Serializable, EditorGUI.PropertyGridConfig.NoHeader))
        {
            MethodInfo? method = target.GetType().GetMethod("OnValidate", BindingFlags.Public | BindingFlags.Instance);
            method?.Invoke(t, null);

            target = t;
        }
    }

    public virtual void OnDisable() { }


    private static readonly Dictionary<Type, Type> s_editorLookup = [];

    [OnAssemblyLoad]
    public static void GenerateLookUp()
    {
        List<Type> typesToSearch = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).ToList();
        List<CustomEditorAttribute> customEditors = [];

        foreach (Type type in typesToSearch)
        {
            CustomEditorAttribute? attribute = type.GetCustomAttribute<CustomEditorAttribute>();

            if (attribute == null)
                continue;

            if (type != typeof(ScriptedEditor))
            {
                Debug.LogWarning($"{nameof(CustomEditorAttribute)} is not valid on type '{type}'. Attribute is only valid on types inheriting from {nameof(ScriptedEditor)}. Attribute will be ignored.");
                continue;
            }

            if (s_editorLookup.TryGetValue(attribute.Type, out Type? oldType))
                Debug.LogError($"Custom Editor Overwritten. {attribute.Type.Name} already has a custom Editor: {oldType.Name}, being overwritten by: {type.Name}");

            s_editorLookup[attribute.Type] = type;
            customEditors.Add(attribute);
        }

        foreach (Type type in typesToSearch)
        {
            Type? baseType = type;
            Type? editor = null;

            while (baseType != null && !s_editorLookup.TryGetValue(baseType, out editor))
                baseType = baseType.BaseType;

            if (editor != null)
                s_editorLookup[type] = editor;
        }
    }

    [OnAssemblyUnload]
    public static void ClearLookUp()
    {
        s_editorLookup.Clear();
    }

    /// <returns>The editor type for that Extension</returns>
    public static Type? GetEditorType(Type type)
    {
        if (s_editorLookup.TryGetValue(type, out Type? editorType))
            return editorType;

        return null;
    }
}
