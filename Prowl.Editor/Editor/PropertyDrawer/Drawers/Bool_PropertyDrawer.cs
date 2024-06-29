using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(bool))]
    public class Bool_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            bool val = (bool)value;
            bool changed = Gui.ActiveGUI.Checkbox(ID + "Val", ref val, -5, 0, out _, EditorGUI.GetInputStyle());
            value = val;
            return changed;
        }
    }

}
