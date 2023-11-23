using System.Reflection;
using Prowl.Runtime;
using JetBrains.Annotations;

namespace Prowl.Editor.PropertyDrawers;

[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public abstract class PropertyDrawer {
    
    private static readonly Dictionary<Type, PropertyDrawer> _propertyDrawerLookup = new();
    protected internal abstract Type PropertyType { get; }
    protected internal abstract bool DrawPropertyInternal(object container, FieldInfo fieldInfo);
    protected internal abstract bool DrawPropertyInternal(object container, PropertyInfo propertyInfo);
    
    internal static bool Draw(object container, FieldInfo fieldInfo)
    {
        if (fieldInfo.FieldType.IsAssignableTo(typeof(EngineObject)))
        {
            // Special case, EngineObject is PropertyDrawerAsset type
            if (_propertyDrawerLookup.TryGetValue(typeof(EngineObject), out PropertyDrawer? assetDrawer))
            {
                bool changed = assetDrawer.DrawPropertyInternal(container, fieldInfo);
                if(changed) (container as EngineObject)?.OnValidate();
                return changed;
            }
            // er, we dont have EngineObject drawer
            // thats fine i guess fallback to whatever other drawer we have
        }

        if (_propertyDrawerLookup.TryGetValue(fieldInfo.FieldType, out PropertyDrawer? propertyDrawer)) {
            return propertyDrawer.DrawPropertyInternal(container, fieldInfo);
        } 
        else {
            // Couldnt find a direct type, maybe theres a base type that has a drawer?
            foreach (KeyValuePair<Type, PropertyDrawer> pair in _propertyDrawerLookup)
            {
                if (pair.Key.IsAssignableFrom(fieldInfo.FieldType))
                {
                    return pair.Value.DrawPropertyInternal(container, fieldInfo);
                }
            }
            //Debug.LogWarning($"Property can't be drawn because there is no property drawer defined for {fieldInfo.FieldType}");
        }
        return false;
    }
    
    internal static bool Draw(object container, PropertyInfo propertyInfo) {

        if(propertyInfo.PropertyType.IsAssignableTo(typeof(EngineObject)))
        {
            // Special case, EngineObject is PropertyDrawerAsset type
            if (_propertyDrawerLookup.TryGetValue(typeof(EngineObject), out PropertyDrawer? assetDrawer))
            {
                bool changed = assetDrawer.DrawPropertyInternal(container, propertyInfo);
                if (changed) (container as EngineObject)?.OnValidate();
                return changed;
            }
            // er, we dont have EngineObject drawer
            // thats fine i guess fallback to whatever other drawer we have
        }

        if(_propertyDrawerLookup.TryGetValue(propertyInfo.PropertyType, out PropertyDrawer? propertyDrawer)) {
            return propertyDrawer.DrawPropertyInternal(container, propertyInfo);
        } 
        else {

            // Couldnt find a direct type, maybe theres a base type that has a drawer?
            foreach (KeyValuePair<Type, PropertyDrawer> pair in _propertyDrawerLookup)
            {
                if (pair.Key.IsAssignableFrom(propertyInfo.PropertyType))
                {
                    return pair.Value.DrawPropertyInternal(container, propertyInfo);
                }
            }

            //Debug.LogWarning($"Property can't be drawn because there is no property drawer defined for {propertyInfo.PropertyType}");
        }
        return false;
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

public abstract class PropertyDrawer<TProperty> : PropertyDrawer {
    
    protected internal sealed override Type PropertyType => typeof(TProperty);
    
    protected internal sealed override bool DrawPropertyInternal(object container, FieldInfo fieldInfo) {
        var value = (TProperty?)fieldInfo.GetValue(container);
        var old = value;
        DrawProperty(ref value, new Property(fieldInfo));
        fieldInfo.SetValue(container, value);
        if (old == null && value == null) return false;
        else if (old == null && value != null) return true;
        else if (old.Equals(value) == false) return true; // Returns true if has been modified
        return false;
    }
    
    protected internal sealed override bool DrawPropertyInternal(object container, PropertyInfo propertyInfo) {
        var value = (TProperty?)propertyInfo.GetValue(container);
        var old = value;
        DrawProperty(ref value, new Property(propertyInfo));
        if(propertyInfo.CanWrite)
            propertyInfo.SetValue(container, value);
        if (old == null && value == null) return false;
        else if (old == null && value != null) return true;
        else if (old.Equals(value) == false) return true; // Returns true if has been modified
        return false;
    }
    
    protected abstract void DrawProperty(ref TProperty? value, Property property);
    
}
