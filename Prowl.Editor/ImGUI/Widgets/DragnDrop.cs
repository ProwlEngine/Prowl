using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Runtime;

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
            if (ImGui.BeginDragDropTarget())
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

            if (ImGui.BeginDragDropTarget())
            {
                object? target = null;
                foreach (var obj in draggedObject)
                    if (obj.GetType().IsAssignableTo(type))
                    {
                        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.25f)));
                        target = obj;
                    }
                if (target != null)
                {
                    ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload("heheboobies");
                    if (!entityPayload.IsNull)
                    {
                        payload = target;
                        draggedObject = null;
                        payloadTag = "";
                        ImGui.EndDragDropTarget();
                        return true;
                    }
                }

                ImGui.EndDragDropTarget();
            }
            return false;
        }

        public static bool Drag(params object[] objs) => Drag("", objs);

        public static bool Drag(string tag = "", params object[] objs)
        {
            // Remove Nulls
            objs = objs.Where(o => o != null).ToArray();
            if (ImGui.BeginDragDropSource())
            {
                draggedObject = objs;
                payloadTag = tag;
                unsafe { ImGui.SetDragDropPayload("heheboobies", null, 0); }
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
                ImGui.TextUnformatted(name);
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }


        //private static object draggedObject;
        //
        //private const string AssetPayload = "ASSETPAYLOAD_";
        //private const string ReferencePayload = "REFERENCEPAYLOAD_";
        //
        //public static bool ReceiveAsset<T>(out AssetRef<T> droppedAsset) where T : EngineObject
        //{
        //    droppedAsset = null;
        //    if (ImGui.BeginDragDropTarget())
        //    {
        //        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.25f)));
        //        ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload(AssetPayload + typeof(T).Name);
        //        if (!entityPayload.IsNull)
        //        {
        //            droppedAsset = new AssetRef<T>((Guid)draggedObject);
        //            ImGui.EndDragDropTarget();
        //            return true;
        //        }
        //
        //        ImGui.EndDragDropTarget();
        //    }
        //    return false;
        //}
        //
        //public static bool ReceiveAsset(out Guid droppedAsset, string typeName)
        //{
        //    droppedAsset = Guid.Empty;
        //    if (ImGui.BeginDragDropTarget())
        //    {
        //        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.25f)));
        //        ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload(AssetPayload + typeName);
        //        if (!entityPayload.IsNull)
        //        {
        //            droppedAsset = (Guid)draggedObject;
        //            ImGui.EndDragDropTarget();
        //            return true;
        //        }
        //
        //        ImGui.EndDragDropTarget();
        //    }
        //    return false;
        //}
        //
        //public static bool ReceiveReference<T>(out T droppedObject) where T : class
        //{
        //    droppedObject = null;
        //    if (ImGui.BeginDragDropTarget())
        //    {
        //        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.25f)));
        //        ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload(ReferencePayload + typeof(T).Name);
        //        if (!entityPayload.IsNull)
        //        {
        //            droppedObject = (T)draggedObject;
        //            ImGui.EndDragDropTarget();
        //            return true;
        //        }
        //
        //        ImGui.EndDragDropTarget();
        //    }
        //    return false;
        //}
        //
        //public static bool OfferAsset<T>(AssetRef<T> offeredAsset) where T : EngineObject
        //{
        //    if (ImGui.BeginDragDropSource())
        //    {
        //        draggedObject = offeredAsset.AssetID;
        //        unsafe { ImGui.SetDragDropPayload(AssetPayload + typeof(T).Name, null, 0); }
        //        ImGui.TextUnformatted(offeredAsset.Name + " - " + offeredAsset.AssetID);
        //        ImGui.EndDragDropSource();
        //        return true;
        //    }
        //    return false;
        //}
        //
        //public static bool OfferAsset(Guid offeredAsset, string typeName)
        //{
        //    if (ImGui.BeginDragDropSource())
        //    {
        //        draggedObject = offeredAsset;
        //        unsafe { ImGui.SetDragDropPayload(AssetPayload + typeName, null, 0); }
        //        ImGui.TextUnformatted(typeName + " - Asset");
        //        ImGui.EndDragDropSource();
        //        return true;
        //    }
        //    return false;
        //}
        //
        //public static bool OfferReference<T>(T offeredObject) where T : class
        //{
        //    if (ImGui.BeginDragDropSource())
        //    {
        //        draggedObject = offeredObject;
        //        unsafe { ImGui.SetDragDropPayload(ReferencePayload + typeof(T).Name, null, 0); }
        //        ImGui.TextUnformatted(typeof(T).Name + " - Instance");
        //        ImGui.EndDragDropSource();
        //        return true;
        //    }
        //    return false;
        //}
    }
}
