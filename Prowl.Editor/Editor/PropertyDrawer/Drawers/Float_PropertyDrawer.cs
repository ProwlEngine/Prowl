using Prowl.Editor.Preferences;
using Prowl.Runtime.GUI;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(float))]
    public class Float_PropertyDrawer : PropertyDrawer
    {
        public override double MinWidth => EditorStylePrefs.Instance.ItemSize * 2;

        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            float val = (float)value;
            bool changed = EditorGUI.InputFloat(ID + "Val", ref val, 0, 0, Size.Percentage(1f), EditorGUI.InputFieldStyle);
            value = val;
            return changed;
        }
    }


}
