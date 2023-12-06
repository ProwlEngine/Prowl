using System.Reflection;
using Prowl.Runtime;
using JetBrains.Annotations;
using HexaEngine.ImGuiNET;
namespace Prowl.Editor.PropertyDrawers;

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public abstract class PropertyDrawer {
    
    private static readonly Dictionary<Type, PropertyDrawer> _propertyDrawerLookup = new();
    protected internal abstract Type PropertyType { get; }
    protected internal abstract bool Draw_Internal(string label, ref object value);


    public static bool Draw(object container, FieldInfo fieldInfo)
    {
        var value = fieldInfo.GetValue(container);
        bool changed = Draw(fieldInfo.Name, ref value);
        if (changed) fieldInfo.SetValue(container, value);
        return changed;
    }

    public static bool Draw(object container, PropertyInfo fieldInfo)
    {
        var value = fieldInfo.GetValue(container);
        bool changed = Draw(fieldInfo.Name, ref value);
        if (changed) fieldInfo.SetValue(container, value);
        return changed;
    }


    public static bool Draw(string label, ref object value)
    {
        var objType = value.GetType();
        bool changed = false;
        ImGui.PushID(label);
        if (_propertyDrawerLookup.TryGetValue(objType, out PropertyDrawer? propertyDrawer))
        {
            changed = propertyDrawer.Draw_Internal(label, ref value);
        }
        else
        {
            foreach (KeyValuePair<Type, PropertyDrawer> pair in _propertyDrawerLookup)
                if (pair.Key.IsAssignableFrom(objType))
                {
                    changed = pair.Value.Draw_Internal(label, ref value);
                    break;
                }
        }
        ImGui.PopID();
        return changed;
    }
    
    public static void ClearLookUp() {
        _propertyDrawerLookup.Clear();
    }

    public static void GenerateLookUp()
    {
        _propertyDrawerLookup.Clear();
        foreach (Assembly editorAssembly in EditorApplication.Instance.ExternalAssemblies.Append(typeof(EditorApplication).Assembly))
        {
            List<Type> derivedTypes = GetDerivedTypes(typeof(PropertyDrawer<>), editorAssembly);
            foreach (Type type in derivedTypes)
            {
                try
                {
                    PropertyDrawer propertyDrawer = Activator.CreateInstance(type) as PropertyDrawer ?? throw new NullReferenceException();
                    if (!_propertyDrawerLookup.TryAdd(propertyDrawer.PropertyType, propertyDrawer))
                        Debug.LogWarning($"Failed to register property drawer for {type.ToString()}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to register property drawer for {type.ToString()}");
                }
            }
        }
    }

    public static List<Type> GetDerivedTypes(Type baseType, Assembly assembly)
    {
        // Get all types from the given assembly
        Type[] types = assembly.GetTypes();
        List<Type> derivedTypes = new List<Type>();

        for (int i = 0, count = types.Length; i < count; i++)
        {
            Type type = types[i];
            if (IsSubclassOf(type, baseType))
            {
                // The current type is derived from the base type,
                // so add it to the list
                derivedTypes.Add(type);
            }
        }

        return derivedTypes;
    }

    public static bool IsSubclassOf(Type type, Type baseType)
    {
        if (type == null || baseType == null || type == baseType)
            return false;

        if (baseType.IsGenericType == false)
        {
            if (type.IsGenericType == false)
                return type.IsSubclassOf(baseType);
        }
        else
        {
            baseType = baseType.GetGenericTypeDefinition();
        }

        type = type.BaseType;
        Type objectType = typeof(object);

        while (type != objectType && type != null)
        {
            Type curentType = type.IsGenericType ?
                type.GetGenericTypeDefinition() : type;
            if (curentType == baseType)
                return true;

            type = type.BaseType;
        }
        return false;
    }

}

public abstract class PropertyDrawer<T> : PropertyDrawer {
    
    protected internal sealed override Type PropertyType => typeof(T);
    
    protected internal sealed override bool Draw_Internal(string label, ref object value) {
        T typedValue = (T)value;
        var old = value;
        bool changed = Draw(label, ref typedValue);
        if (changed) // If the value has been modified, update the original value
            value = typedValue;

        if (old == null && value == null) return false;
        else if (old == null && value != null) return true;
        else if (old.Equals(value) == false) return true; // Returns true if has been modified
        
        return changed;
    }
    
    protected abstract bool Draw(string label, ref T? value);
    
}
