using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(EngineObject))]
    public class EngineObject_PropertyDrawer : PropertyDrawer
    {
        public override double MinWidth => 125;

        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            var value = (EngineObject)targetValue;

            bool changed = false;
            bool pressed = ActiveGUI.IsNodePressed(); // Lets UI know this node can take focus

            ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1, 2);

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
                ActiveGUI.Draw2D.DrawText(value.Name, pos, Color.white * (ActiveGUI.IsNodeHovered() ? 1f : 0.8f));
                if (ActiveGUI.IsNodeHovered() && ActiveGUI.IsPointerDoubleClick(MouseButton.Left))
                    GlobalSelectHandler.Select(value);
            }

            // Drag and drop support
            if (DragnDrop.Drop(out var instance, targetType))
            {
                targetValue = instance as EngineObject;
                changed = true;
            }

            // support looking for components on dropped GameObjects
            if (targetType.IsAssignableTo(typeof(MonoBehaviour)) && DragnDrop.Drop(out GameObject go))
            {
                var component = go.GetComponent(targetType);
                if (component != null)
                {
                    targetValue = component;
                    changed = true;
                }
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
