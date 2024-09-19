// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;

namespace Prowl.Editor;

public static class DragnDrop
{
    private static object[]? draggedObject;
    private static string payloadTag = "";


    public static bool Peek<T>(out T? payload, string tag = "")
    {
        payload = default;
        if (Peek(out var objPayload, typeof(T), tag))
        {
            payload = (T)objPayload;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Peek at what object you would receive if you were to call Drop<T>
    /// </summary>
    public static bool Peek(out object? payload, Type type, string tag = "")
    {
        if (Gui.ActiveGUI.PreviousInteractable == null)
            throw new Exception("No Previous Interactable");

        payload = default;
        if (draggedObject == null) return false;
        bool hasTag = !string.IsNullOrEmpty(tag);
        if (hasTag && payloadTag != tag) return false;
        if (Gui.ActiveGUI.DragDrop_Target())
        {
            foreach (var obj in draggedObject)
            {
                if (obj.GetType().IsAssignableTo(type))
                {
                    payload = obj;
                    return true;
                }
            }
        }
        return false;
    }

    public static bool Drop<T>(out T? payload, string tag = "")
    {
        payload = default;
        if (Drop(out var objPayload, typeof(T), tag))
        {
            payload = (T)objPayload;
            return true;
        }
        return false;
    }

    public static bool Drop(out object? payload, Type type, string tag = "")
    {
        if (Gui.ActiveGUI.PreviousInteractable == null)
            throw new Exception("No Previous Interactable");

        payload = default;
        if (draggedObject == null) return false;
        bool hasTag = !string.IsNullOrEmpty(tag);
        if (hasTag && payloadTag != tag) return false;

        if (Gui.ActiveGUI.DragDrop_Target())
        {
            object? target = null;
            foreach (var obj in draggedObject)
                if (obj.GetType().IsAssignableTo(type))
                    target = obj;

            if (target != null)
            {
                var oldZ = Gui.ActiveGUI.CurrentZIndex;
                Gui.ActiveGUI.SetZIndex(1000000);

                var rect = Gui.ActiveGUI.PreviousInteractable!.Value.Rect;
                rect.Expand(1);
                Gui.ActiveGUI.Draw2D.DrawRect(rect, EditorStylePrefs.Instance.DropHighlight, 2, 8);

                Gui.ActiveGUI.SetZIndex(oldZ);

                if (Gui.ActiveGUI.DragDrop_Accept())
                {
                    payload = target;
                    draggedObject = null;
                    payloadTag = "";
                    return true;
                }
            }
        }
        return false;
    }

    public static bool Drag(params object[] objs) => Drag("", objs);

    public static bool Drag(string tag = "", params object[] objs)
    {
        if (Gui.ActiveGUI.PreviousInteractable == null)
            throw new Exception("No Previous Interactable");

        if (OnBeginDrag(out var node))
        {
            using (node.Enter())
            {
                node.Width(20).Height(20);

                Gui.ActiveGUI.Draw2D.DrawList.PushClipRectFullScreen();
                Gui.ActiveGUI.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.BoxesPacking, 20, node.LayoutData.InnerRect.Position, Color.white);
                Gui.ActiveGUI.Draw2D.DrawList.PopClipRect();
            }
            SetPayload(tag, objs);
            return true;
        }
        return false;
    }

    public static bool OnBeginDrag(out LayoutNode? node)
    {
        if (Gui.ActiveGUI.PreviousInteractable == null)
            throw new Exception("No Previous Interactable");

        if (Gui.ActiveGUI.DragDrop_Source(out node))
            return true;
        return false;
    }

    public static void SetPayload(params object[] objs) => SetPayload("", objs);
    public static void SetPayload(string tag = "", params object[] objs)
    {
        // Remove Nulls
        objs = objs.Where(o => o != null).ToArray();

        draggedObject = objs;
        payloadTag = tag;
        // Constract a name from all the types
        string name = "";
        foreach (var obj in objs)
        {
            // AssetRef is Special use InstanceType
            if (obj is IAssetRef assetRef)
                name += assetRef.InstanceType.Name + ", ";
            else
                name += obj.GetType().Name + ", ";
        }
    }
}
