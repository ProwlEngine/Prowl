// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;

using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(IAssetRef))]
public class IAssetRef_PropertyDrawer : PropertyDrawer
{
    public override double MinWidth => 125;
    public static Guid assignedGUID;
    public static ushort assignedFileID;
    public static ulong guidAssignedToID;

    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

        IAssetRef value = (IAssetRef)targetValue;

        bool changed = false;
        ulong assetDrawerID = ActiveGUI.CurrentNode.ID;
        if (guidAssignedToID != 0 && guidAssignedToID == assetDrawerID)
        {
            value.AssetID = assignedGUID;
            value.FileID = assignedFileID;
            assignedGUID = Guid.Empty;
            assignedFileID = 0;
            guidAssignedToID = 0;
            changed = true;
        }

        ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1, 2);

        using (ActiveGUI.Node(ID + "Selector").MaxWidth(ItemSize).ExpandHeight().Enter())
        {
            var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
            var centerY = (ActiveGUI.CurrentNode.LayoutData.InnerRect.height / 2) - (20 / 2);
            pos += new Vector2(5, centerY + 3);
            if (ActiveGUI.IsNodePressed())
            {
                new AssetSelectorWindow(value.InstanceType, (guid, fileid) => { assignedGUID = guid; guidAssignedToID = assetDrawerID; assignedFileID = fileid; });
            }
            else if (ActiveGUI.IsNodeHovered())
            {
                ActiveGUI.Draw2D.DrawRectFilled(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);
            }
            ActiveGUI.Draw2D.DrawText(FontAwesome6.MagnifyingGlass, pos, Color.white * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f));
        }

        // Thumbnail for Textures
        if (value.InstanceType == typeof(Texture2D) && value.IsAvailable)
        {
            using (ActiveGUI.Node(ID + "Thumbnail").MaxWidth(ItemSize + 5).ExpandHeight().Enter())
            {
                var tex = value.GetInstance();
                if (tex != null)
                {
                    var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
                    ActiveGUI.Draw2D.DrawImage((Texture2D)tex, pos, new Vector2(ItemSize), true);
                }
            }
        }

        using (ActiveGUI.Node(ID + "Asset").ExpandHeight().Enter())
        {
            var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
            var centerY = (ActiveGUI.CurrentNode.LayoutData.InnerRect.height / 2) - (20 / 2);
            pos += new Vector2(0, centerY + 3);
            bool pressed = ActiveGUI.IsNodePressed(); // lets UI know this can take focus
            if (value.IsExplicitNull || value.IsRuntimeResource)
            {
                string text = value.IsExplicitNull ? "(Null)" : "(Runtime)" + value.Name;
                var col = Color.white * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f);
                if (value.IsExplicitNull)
                    col = EditorStylePrefs.Red * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f);
                ActiveGUI.Draw2D.DrawText(Font.DefaultFont, text, 20, pos, col, 0, ActiveGUI.CurrentNode.LayoutData.Rect);
            }
            else if (AssetDatabase.TryGetFile(value.AssetID, out var assetPath))
            {
                string name = value.IsAvailable ? value.Name : assetPath.Name;
                ActiveGUI.Draw2D.DrawText(Font.DefaultFont, name, 20, pos, Color.white * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f), 0, ActiveGUI.CurrentNode.LayoutData.Rect);
                if (pressed)
                    AssetDatabase.Ping(value.AssetID);
            }

            if (ActiveGUI.IsNodeHovered() && ActiveGUI.IsPointerDoubleClick(MouseButton.Left))
                GlobalSelectHandler.Select(value);

            // Drag and drop support
            if (DragnDrop.Drop(out var instance, value.InstanceType))
            {
                // SetInstance() will also set the AssetID if the instance is an asset
                value.SetInstance(instance);
                changed = true;
            }

            if (ActiveGUI.IsNodeActive() || ActiveGUI.IsNodeFocused())
            {
                ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, 1, (float)EditorStylePrefs.Instance.ButtonRoundness);

                if (ActiveGUI.IsKeyDown(Key.Delete))
                {
                    value.AssetID = Guid.Empty;
                    value.FileID = 0;
                    changed = true;
                }
            }
        }

        targetValue = value;
        return changed;
    }
}
