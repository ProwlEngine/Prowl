using Hexa.NET.ImGui;
using Prowl.Editor.ImGUI.Widgets;

namespace Prowl.Editor.PropertyDrawers;

public abstract class PropertyDrawerOrderable<T> : PropertyDrawer<T> where T : class
{
    protected abstract int GetCount(T value);
    protected abstract object GetElement(T value, int index);
    protected abstract void SetElement(T value, int index, object element);
    protected abstract void RemoveElement(ref T value, int index);
    protected abstract void AddElement(ref T value);

    protected override bool Draw(string label, ref T? value, float width)
    {
        bool changed = false;
        if (ImGui.TreeNode(label))
        {
            ImGui.PushID(label);
            int arrayID = ImGui.GetItemID();
            ImGui.Indent();

            // Get current y so we can draw a background rectangle
            var startY = ImGui.GetCursorScreenPos().Y;
            var startX = ImGui.GetCursorScreenPos().X - 3;
            ImGui.Indent();

            width -= 28 + 2;

            for (int i = 0; i < GetCount(value); i++)
            {
                ImGui.PushID(i);
                var element = GetElement(value, i);

                // Draw the Drag icon
                ImGui.Selectable("=", new System.Numerics.Vector2(28, 0));
                if (ImGui.IsItemHovered())
                {
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    {
                        // Show the context menu on right-click
                        ImGui.OpenPopup("ContextMenu");
                    }
                    DragnDrop.Drag(arrayID.ToString(), i);
                }

                if (DragnDrop.Drop<int>(out var dropped, arrayID.ToString()))
                {
                    // Swap the elements
                    var draggedElement = GetElement(value, dropped);
                    SetElement(value, dropped, element);
                    SetElement(value, i, draggedElement);
                    changed = true;
                }

                ImGui.SameLine();
                ImGui.Indent(28);

                if (PropertyDrawer.Draw($"Element {i}", ref element, width))
                {
                    SetElement(value, i, element);
                    changed = true;
                }
                ImGui.Unindent(28);

                // Context menu for deletion
                if (ImGui.BeginPopup("ContextMenu"))
                {
                    if (ImGui.MenuItem("Delete"))
                    {
                        RemoveElement(ref value, i);
                        changed = true;
                    }
                    ImGui.EndPopup();
                }

                ImGui.PopID();
            }

            ImGui.Unindent();

            // Move two pixels up so the button is aligned with the tree node
            if (ImGui.Button("Add Element", new(-1, 0)))
            {
                AddElement(ref value);
                changed = true;
            }

            // Draw a background rectangle
            var endY = ImGui.GetCursorScreenPos().Y;
            var style = ImGui.GetStyle();
            var color = style.Colors[(int)ImGuiCol.FrameBg];
            ImGui.GetWindowDrawList().AddLine(new System.Numerics.Vector2(startX, startY), new System.Numerics.Vector2(startX, endY), ImGui.ColorConvertFloat4ToU32(color), 2);
            ImGui.GetWindowDrawList().AddLine(new System.Numerics.Vector2(startX, endY), new System.Numerics.Vector2(startX + 9999, endY), ImGui.ColorConvertFloat4ToU32(color), 2);

            ImGui.Unindent();
            ImGui.PopID();
            ImGui.TreePop();
            ImGui.Columns(1);
        }
        return changed;
    }
}

public class PropertyDrawerArray : PropertyDrawerOrderable<Array>
{
    protected override int GetCount(Array value) => value.Length;
    protected override object GetElement(Array value, int index) => value.GetValue(index);
    protected override void SetElement(Array value, int index, object element) => value.SetValue(element, index);
    protected override void RemoveElement(ref Array value, int index)
    {
        var elementType = value.GetType().GetElementType();
        var newArray = Array.CreateInstance(elementType, value.Length - 1);
        Array.Copy(value, 0, newArray, 0, index);
        Array.Copy(value, index + 1, newArray, index, value.Length - index - 1);
        value = newArray;
    }
    protected override void AddElement(ref Array value)
    {
        var elementType = value.GetType().GetElementType();
        var newArray = Array.CreateInstance(elementType, value.Length + 1);
        Array.Copy(value, newArray, value.Length);
        value = newArray;
    }
}

public class PropertyDrawerList : PropertyDrawerOrderable<System.Collections.IList>
{
    protected override int GetCount(System.Collections.IList value) => value.Count;
    protected override object GetElement(System.Collections.IList value, int index) => value[index];
    protected override void SetElement(System.Collections.IList value, int index, object element) => value[index] = element;
    protected override void RemoveElement(ref System.Collections.IList value, int index) => value.RemoveAt(index);
    protected override void AddElement(ref System.Collections.IList value)
    {
        var elementType = value.GetType().GetGenericArguments()[0];
        var element = Activator.CreateInstance(elementType);
        value.Add(element);
    }
}