using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows.CustomEditors
{
    [CustomEditor(typeof(Transform))]
    public class TransformEditor : ScriptedEditor
    {
        public override void OnInspectorGUI()
        {
            var t = target as Transform;

            ImGui.PushID(t.GetHashCode());

            bool changed = false;
            changed |= PropertyDrawer.Draw(t, typeof(Transform).GetProperty("Position")!);
            changed |= PropertyDrawer.Draw(t, typeof(Transform).GetProperty("Rotation")!);
            changed |= PropertyDrawer.Draw(t, typeof(Transform).GetProperty("Scale")!);
            if (changed)
                t.OnValidate();

            ImGui.PopID();
        }

    }
}
