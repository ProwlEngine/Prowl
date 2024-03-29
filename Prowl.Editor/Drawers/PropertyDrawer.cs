using Hexa.NET.ImGui;
using Prowl.Editor.Utilities;
using Prowl.Runtime;
using System.Reflection;
using System.Text;
namespace Prowl.Editor.PropertyDrawers;

public abstract class PropertyDrawer {
    
    private static readonly Dictionary<Type, PropertyDrawer> _propertyDrawerLookup = new();
    protected internal abstract Type PropertyType { get; }
    protected internal abstract bool Draw_Internal(string label, ref object value, float width);


    public static bool Draw(object container, FieldInfo fieldInfo, float width = -1, string? label = null)
    {
        if (fieldInfo == null) return false;
        if (width == -1) width = ImGui.GetContentRegionAvail().X;
        var value = fieldInfo.GetValue(container);
        bool changed = Draw(label ?? fieldInfo.Name, ref value, width);
        if (changed) fieldInfo.SetValue(container, value);
        return changed;
    }

    public static bool Draw(object container, PropertyInfo propertyInfo, float width = -1, string? label = null)
    {
        if (propertyInfo == null) return false;
        if (width == -1) width = ImGui.GetContentRegionAvail().X;
        var value = propertyInfo.GetValue(container);
        bool changed = Draw(label ?? propertyInfo.Name, ref value, width);
        if (changed) propertyInfo.SetValue(container, value);
        return changed;
    }


    public static bool Draw(string label, ref object value, float width = -1)
    {
        if (value == null) return false;
        if (width == -1) width = ImGui.GetContentRegionAvail().X;
        var objType = value.GetType();
        bool changed = false;
        ImGui.PushID(label);
        PropertyDrawer? propertyDrawer;
        if (_propertyDrawerLookup.TryGetValue(objType, out propertyDrawer))
        {
            changed = propertyDrawer.Draw_Internal(label, ref value, width);
        }
        // Maybe we have an editor for its base type?
        else if (_propertyDrawerLookup.TryGetValue(objType.BaseType, out propertyDrawer))
        {
            changed = propertyDrawer.Draw_Internal(label, ref value, width);
        }
        else
        {
            // Last Resort
            foreach (KeyValuePair<Type, PropertyDrawer> pair in _propertyDrawerLookup)
                if (pair.Key.IsAssignableFrom(objType))
                {
                    changed = pair.Value.Draw_Internal(label, ref value, width);
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
            List<Type> derivedTypes = EditorUtils.GetDerivedTypes(typeof(PropertyDrawer<>), editorAssembly);
            foreach (Type type in derivedTypes)
            {
                try
                {
                    PropertyDrawer propertyDrawer = Activator.CreateInstance(type) as PropertyDrawer ?? throw new NullReferenceException();
                    if (!_propertyDrawerLookup.TryAdd(propertyDrawer.PropertyType, propertyDrawer))
                        Debug.LogWarning($"Failed to register property drawer for {type.ToString()}");
                }
                catch
                {
                    Debug.LogWarning($"Failed to register property drawer for {type.ToString()}");
                }
            }
        }
    }

}

public abstract class PropertyDrawer<T> : PropertyDrawer {
    
    protected internal sealed override Type PropertyType => typeof(T);
    
    protected internal sealed override bool Draw_Internal(string label, ref object value, float width) {
        T typedValue = (T)value;
        var old = value;
        bool changed = Draw(label, ref typedValue, width);
        if (changed) // If the value has been modified, update the original value
            value = typedValue;

        if (old == null && value == null) return false;
        else if (old == null && value != null) return true;
        else if (old.Equals(value) == false) return true; // Returns true if has been modified
        
        return changed;
    }
    
    protected abstract bool Draw(string label, ref T? value, float width);
    
    protected void DrawLabel(string label, ref float width)
    {
        ImGui.Columns(2, false);
        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1f));
        ImGui.Text(RuntimeUtils.Prettify(label));
        ImGui.PopStyleColor();
        var w = width / 2.5f;
        ImGui.SetColumnWidth(0, w);
        width -= w;
        ImGui.NextColumn();
    }
}
