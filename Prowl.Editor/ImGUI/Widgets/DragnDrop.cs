using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;

namespace Prowl.Editor.ImGUI.Widgets
{

    public static class DragnDrop
    {
        private static object[] draggedObject;
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
            payload = default;
            if (draggedObject == null) return false;
            bool hasTag = !string.IsNullOrEmpty(tag);
            if(hasTag && payloadTag != tag) return false;
            if (Gui.ActiveGUI.DragDropTarget())
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
            payload = default;
            if (draggedObject == null) return false;
            bool hasTag = !string.IsNullOrEmpty(tag);
            if (hasTag && payloadTag != tag) return false;

            if (Gui.ActiveGUI.DragDropTarget())
            {
                object? target = null;
                foreach (var obj in draggedObject)
                    if (obj.GetType().IsAssignableTo(type))
                        target = obj;

                if (target != null)
                {
                    _ = Gui.ActiveGUI.AcceptDragDrop();
                    payload = target;
                    draggedObject = null;
                    payloadTag = "";
                    return true;
                }
            }
            return false;
        }

        public static bool Drag(params object[] objs) => Drag("", objs);
                                
        public static bool Drag(string tag = "", params object[] objs)
        {
            if (OnBeginDrag(out var node))
            {
                using (node.Enter())
                {
                    node.Width(20).Height(20);

                    Gui.ActiveGUI.DrawText(UIDrawList.DefaultFont, FontAwesome6.BoxesPacking, 20, node.LayoutData.InnerRect.Position, Color.white);
                }
                SetPayload(tag, objs);
                return true;
            }
            return false;
        }

        public static bool OnBeginDrag(out LayoutNode? node)
        {
            if (Gui.ActiveGUI.DragDropSource(out node))
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
}
