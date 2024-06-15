using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(Vector2))]
    public class Vector2_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            Vector2 val = (Vector2)value;
            bool changed = EditorGUI.InputDouble(ID + "X", ref val.x, 0, 0, 0, EditorGUI.VectorXStyle);
            changed |= EditorGUI.InputDouble(ID + "Y", ref val.y, 0, 0, 0, EditorGUI.VectorYStyle);
            value = val;
            return changed;
        }
    }


}
