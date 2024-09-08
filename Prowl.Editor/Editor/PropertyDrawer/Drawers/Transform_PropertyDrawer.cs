// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(Transform))]
    public class Transform_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            var value = (Transform)targetValue;

            bool changed = false;

            ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1, 2);

            bool pressed = ActiveGUI.IsNodePressed(); // Lets UI know this node can take focus

            var pos = ActiveGUI.CurrentNode.LayoutData.GlobalContentPosition;
            var centerY = (ActiveGUI.CurrentNode.LayoutData.InnerRect.height / 2) - (20 / 2);
            pos += new Vector2(5, centerY + 3);
            if (value == null)
            {
                string text = "(Null) " + targetType.Name;
                var col = EditorStylePrefs.Red * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f);
                ActiveGUI.Draw2D.DrawText(text, pos, col);
            }
            else
            {
                ActiveGUI.Draw2D.DrawText(value.gameObject.Name + " (Transform)", pos, Color.white * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f));
                if (ActiveGUI.IsNodeHovered() && ActiveGUI.IsPointerDoubleClick(MouseButton.Left))
                    GlobalSelectHandler.Select(value);
            }

            // Drag and drop support
            if (DragnDrop.Drop(out Transform instance))
            {
                targetValue = instance;
                changed = true;
            }

            // support looking for components on dropped GameObjects
            if (DragnDrop.Drop(out GameObject go))
            {
                targetValue = go.Transform;
                changed = true;
            }

            if (ActiveGUI.IsNodeActive() || ActiveGUI.IsNodeFocused())
            {
                ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, 1, (float)EditorStylePrefs.Instance.ButtonRoundness);

                if (ActiveGUI.IsKeyDown(Key.Delete))
                {
                    targetValue = null;
                    changed = true;
                }
            }

            return changed;
        }
    }


}
