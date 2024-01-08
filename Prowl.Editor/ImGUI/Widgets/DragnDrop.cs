using HexaEngine.ImGuiNET;
using Prowl.Runtime;

namespace Prowl.Editor.ImGUI.Widgets
{
    public static class DragnDrop
    {
        private static object draggedObject;

        private const string AssetPayload = "ASSETPAYLOAD_";
        private const string ReferencePayload = "REFERENCEPAYLOAD_";

        public static bool ReceiveAsset<T>(out AssetRef<T> droppedAsset) where T : EngineObject
        {
            droppedAsset = null;
            if (ImGui.BeginDragDropTarget())
            {
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.25f)));
                ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload(AssetPayload + typeof(T).Name);
                if (!entityPayload.IsNull)
                {
                    droppedAsset = new AssetRef<T>((Guid)draggedObject);
                    ImGui.EndDragDropTarget();
                    return true;
                }

                ImGui.EndDragDropTarget();
            }
            return false;
        }

        public static bool ReceiveAsset(out Guid droppedAsset, string typeName)
        {
            droppedAsset = Guid.Empty;
            if (ImGui.BeginDragDropTarget())
            {
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.25f)));
                ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload(AssetPayload + typeName);
                if (!entityPayload.IsNull)
                {
                    droppedAsset = (Guid)draggedObject;
                    ImGui.EndDragDropTarget();
                    return true;
                }

                ImGui.EndDragDropTarget();
            }
            return false;
        }

        public static bool ReceiveReference<T>(out T droppedObject) where T : class
        {
            droppedObject = null;
            if (ImGui.BeginDragDropTarget())
            {
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.25f)));
                ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload(ReferencePayload + typeof(T).Name);
                if (!entityPayload.IsNull)
                {
                    droppedObject = (T)draggedObject;
                    ImGui.EndDragDropTarget();
                    return true;
                }

                ImGui.EndDragDropTarget();
            }
            return false;
        }

        public static bool OfferAsset<T>(AssetRef<T> offeredAsset) where T : EngineObject
        {
            if (ImGui.BeginDragDropSource())
            {
                draggedObject = offeredAsset.AssetID;
                unsafe { ImGui.SetDragDropPayload(AssetPayload + typeof(T).Name, null, 0); }
                ImGui.TextUnformatted(offeredAsset.Name + " - " + offeredAsset.AssetID);
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }

        public static bool OfferAsset(Guid offeredAsset, string typeName)
        {
            if (ImGui.BeginDragDropSource())
            {
                draggedObject = offeredAsset;
                unsafe { ImGui.SetDragDropPayload(AssetPayload + typeName, null, 0); }
                ImGui.TextUnformatted(typeName + " - Asset");
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }

        public static bool OfferReference<T>(T offeredObject) where T : class
        {
            if (ImGui.BeginDragDropSource())
            {
                draggedObject = offeredObject;
                unsafe { ImGui.SetDragDropPayload(ReferencePayload + typeof(T).Name, null, 0); }
                ImGui.TextUnformatted(typeof(T).Name + " - Instance");
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }
    }
}
