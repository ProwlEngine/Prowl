using Prowl.Editor.Assets;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows.CustomEditors
{
    [CustomEditor(typeof(GuiStyle))]
    public class GuiStyleEditor : ScriptedEditor
    {
        private Action onChange;

        public GuiStyleEditor() { }

        public GuiStyleEditor(GuiStyle style, Action onChange)
        {
            base.target = style;
            this.onChange = onChange;
        }

        public override void OnInspectorGUI()
        {
            var style = (GuiStyle)target;
            style ??= new GuiStyle();

            bool changed = false;
            foreach (var field in RuntimeUtils.GetSerializableFields(style))
                if (PropertyDrawer.Draw(style, field))
                {
                    style.OnValidate();
                    changed = true;
                }

            if (changed)
                onChange?.Invoke();
        }

    }
}
