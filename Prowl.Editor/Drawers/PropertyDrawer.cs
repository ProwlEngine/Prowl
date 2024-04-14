using Hexa.NET.ImGui;
using Prowl.Editor.ImGUI.Widgets;
using Prowl.Editor.Utilities;
using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.Reflection;
namespace Prowl.Editor.PropertyDrawers;

public abstract class PropertyDrawer {
    
    private static readonly Dictionary<Type, PropertyDrawer> _propertyDrawerLookup = new();
    protected internal abstract Type PropertyType { get; }
    protected internal abstract bool Draw_Internal(string label, ref object value, float width);

    [OnAssemblyUnload]
    public static void ClearLookUp()
    {
        _propertyDrawerLookup.Clear();
    }

    [OnAssemblyLoad]
    public static void GenerateLookUp()
    {
        _propertyDrawerLookup.Clear();
        foreach (Assembly editorAssembly in AssemblyManager.ExternalAssemblies.Append(typeof(Program).Assembly))
        {
            List<Type> derivedTypes = EditorUtils.GetDerivedTypes(typeof(PropertyDrawer<>), editorAssembly);
            foreach (Type type in derivedTypes)
            {
                if (type.IsAbstract || type.IsGenericType) continue;

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

    public static bool Draw(object container, FieldInfo fieldInfo, float width = -1, string? label = null)
    {
        if (fieldInfo == null) return false;

        if (Attribute.IsDefined(fieldInfo, typeof(HideInInspectorAttribute)))
            return false;

        var attributes = fieldInfo.GetCustomAttributes(true);
        var imGuiAttributes = attributes.Where(attr => attr is IImGUIAttri).Cast<IImGUIAttri>();
        bool doDraw = EditorGui.HandleBeginImGUIAttributes(container, imGuiAttributes);
        bool changed = false;
        if (doDraw)
        {
            if (width == -1) width = ImGui.GetContentRegionAvail().X;

            if (fieldInfo.FieldType.IsAssignableTo(typeof(EngineObject)))
                return DrawEngineObjectField(container, label ?? fieldInfo.Name, fieldInfo, ref width);

            if (fieldInfo.FieldType.IsAssignableTo(typeof(Transform)))
                return DrawTransformField(container, label ?? fieldInfo.Name, fieldInfo, ref width);

            var value = fieldInfo.GetValue(container);
            if (value == null)
            {
                DrawNullField(label ?? fieldInfo.Name, width);
                return false;
            }

            changed = Draw(label ?? fieldInfo.Name, ref value, fieldInfo.FieldType, width);
            if (changed) fieldInfo.SetValue(container, value);
        }
        EditorGui.HandleEndImGUIAttributes(imGuiAttributes);
        return changed;
    }

    public static bool Draw(object container, PropertyInfo propertyInfo, float width = -1, string? label = null)
    {
        if (propertyInfo == null) return false;
        if (width == -1) width = ImGui.GetContentRegionAvail().X;

        var value = propertyInfo.GetValue(container);
        if (value == null)
        {
            DrawNullField(label ?? propertyInfo.Name, width);
            return false;
        }

        bool changed = Draw(label ?? propertyInfo.Name, ref value, propertyInfo.PropertyType, width);
        if (changed) propertyInfo.SetValue(container, value);
        return changed;
    }

    protected static bool Draw(string label, ref object value, Type objType, float width = -1)
    {
        if (width == -1) width = ImGui.GetContentRegionAvail().X;
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
            // Look for an Editor for a Base Type
            bool found = false;
            foreach (KeyValuePair<Type, PropertyDrawer> pair in _propertyDrawerLookup)
                if (pair.Key.IsAssignableFrom(objType))
                {
                    found = true;
                    changed = pair.Value.Draw_Internal(label, ref value, width);
                    break;
                }

            if (!found)
            {
                // Nothing found, Lets just draw the object ourselves with a "Default" drawer
                var fields = RuntimeUtils.GetSerializableFields(value);
                if (fields.Length != 0)
                {
                    if (ImGui.TreeNode(label))
                    {
                        ImGui.Indent();
                        foreach (var field in fields)
                            changed |= Draw(value, field);
                        ImGui.Unindent();

                        changed |= EditorGui.HandleAttributeButtons(value);
                        ImGui.TreePop();
                    }
                }
            }
        }
        ImGui.PopID();
        return changed;
    }

    protected static bool DrawEngineObjectField(object container, string label, FieldInfo field, ref float width)
    {
        bool changed = false;
        DrawLabel(label, ref width);

        EngineObject value = (EngineObject)field.GetValue(container);
        if (value == null)
        {
            var pos = ImGui.GetCursorPos();
            ImGui.TextDisabled("null");
            ImGui.SetCursorPos(pos);
            ImGui.Selectable("##null", new System.Numerics.Vector2(width, 21));
            GUIHelper.ItemRectFilled(0.9f, 0.1f, 0.1f, 0.3f);
        }
        else
        {
            ImGui.Selectable(value.Name, new System.Numerics.Vector2(width, 21));
            GUIHelper.ItemRectFilled(0.1f, 0.1f, 0.9f, 0.3f);

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                GlobalSelectHandler.Select(value);
        }

        // Drag and drop support
        if (DragnDrop.Drop(out var instance, field.FieldType))
        {
            field.SetValue(container, instance);
            changed = true;
        }

        // support looking for components on dropped GameObjects
        if (field.FieldType == typeof(MonoBehaviour) && DragnDrop.Drop(out GameObject go))
        {
            var component = go.GetComponent(field.FieldType);
            if (component != null)
            {
                field.SetValue(container, component);
                changed = true;
            }
        }

        ImGui.Columns(1);
        return changed;
    }

    protected static bool DrawTransformField(object container, string label, FieldInfo field, ref float width)
    {
        bool changed = false;
        DrawLabel(label, ref width);

        Transform value = (Transform)field.GetValue(container);
        if (value == null)
        {
            var pos = ImGui.GetCursorPos();
            ImGui.TextDisabled("null");
            ImGui.SetCursorPos(pos);
            ImGui.Selectable("##null", new System.Numerics.Vector2(width, 21));
            GUIHelper.ItemRectFilled(0.9f, 0.1f, 0.1f, 0.3f);
        }
        else
        {
            ImGui.Selectable(value.gameObject.Name, new System.Numerics.Vector2(width, 21));
            GUIHelper.ItemRectFilled(0.1f, 0.1f, 0.9f, 0.3f);

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                GlobalSelectHandler.Select(value);
        }

        // Drag and drop support
        if (DragnDrop.Drop(out Transform instance))
        {
            field.SetValue(container, instance);
            changed = true;
        }

        // support looking for components on dropped GameObjects
        if (DragnDrop.Drop(out GameObject go))
        {
            field.SetValue(container, go.Transform);
            changed = true;
        }

        ImGui.Columns(1);
        return changed;
    }

    private static void DrawNullField(string name, float width)
    {
        float w = width;
        DrawLabel(name, ref w);
        ImGui.SetNextItemWidth(width);
        ImGui.TextDisabled("null");
        ImGui.Columns(1);
    }

    protected static void DrawLabel(string label, ref float width)
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
}
