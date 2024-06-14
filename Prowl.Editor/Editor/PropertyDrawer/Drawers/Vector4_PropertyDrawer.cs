using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(Vector4))]
    public class Vector4_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

            Vector4 val = (Vector4)value;
            bool changed = EditorGUI.InputDouble(ID + "X", ref val.x, 0, 0, 0, EditorGUI.VectorXStyle);
            changed |= EditorGUI.InputDouble(ID + "Y", ref val.y, 0, 0, 0, EditorGUI.VectorYStyle);
            changed |= EditorGUI.InputDouble(ID + "Z", ref val.z, 0, 0, 0, EditorGUI.VectorZStyle);
            changed |= EditorGUI.InputDouble(ID + "W", ref val.w, 0, 0, 0, EditorGUI.InputFieldStyle);
            value = val;
            return changed;
        }
    }


}
