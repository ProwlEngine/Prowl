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

            PropertyDrawer.Draw(t, typeof(Transform).GetProperty("Position")!);
            PropertyDrawer.Draw(t, typeof(Transform).GetProperty("Rotation")!);
            PropertyDrawer.Draw(t, typeof(Transform).GetProperty("Scale")!);

            ImGui.PopID();
        }

    }
}
