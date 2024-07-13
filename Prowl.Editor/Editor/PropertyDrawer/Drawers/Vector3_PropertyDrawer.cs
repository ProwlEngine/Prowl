using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(Vector3))]
    public class Vector3_PropertyDrawer : PropertyDrawer
    {
        public override double MinWidth => 150;

        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            Vector3 val = (Vector3)value;
            bool changed = EditorGUI.InputDouble(ID + "X", ref val.x, 0, 0, 0, EditorGUI.VectorXStyle);
            changed |= EditorGUI.InputDouble(ID + "Y", ref val.y, 0, 0, 0, EditorGUI.VectorYStyle);
            changed |= EditorGUI.InputDouble(ID + "Z", ref val.z, 0, 0, 0, EditorGUI.VectorZStyle);
            value = val;
            return changed;
        }
    }


}
