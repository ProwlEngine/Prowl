using HexaEngine.ImGuiNET;
using System;
using System.Reflection;

namespace Prowl.Runtime
{
    public interface IImGUIAttri
    {
        public void Draw();
        public void End();
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class SpaceAttribute : Attribute, IImGUIAttri
    {
        public void Draw() => ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
        public void End(){}
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class TextAttribute(string text) : Attribute, IImGUIAttri
    {
        public void Draw() => ImGui.Text(text);
        public void End() { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class SeperatorAttribute : Attribute, IImGUIAttri
    {
        public void Draw() => ImGui.Separator();
        public void End() { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SameLineAttribute : Attribute, IImGUIAttri
    {
        public void Draw() => ImGui.SameLine();
        public void End() { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class DisabledAttribute : Attribute, IImGUIAttri
    {
        public void Draw() => ImGui.BeginDisabled();
        public void End() => ImGui.EndDisabled();
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class StartGroupAttribute(string name, float height = 100f, float headerSize = 1f, bool collapsable = true) : Attribute, IImGUIAttri
    {
        public void Draw()
        {
            GUIHelper.TextCenter(name, headerSize);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2);
            ImGui.BeginChild(name, new System.Numerics.Vector2(-1, height), true, collapsable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoCollapse);
        }
        public void End() { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class EndGroupAttribute : Attribute, IImGUIAttri
    {
        public void Draw() { }
        public void End() => ImGui.EndChild();
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class TooltipAttribute(string tooltip) : Attribute, IImGUIAttri
    {
        public void Draw() { }
        public void End() => GUIHelper.Tooltip(tooltip);
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ImGUIButtonAttribute(string buttonText) : Attribute
    {
        internal string buttonText = buttonText;

        public static bool DrawButtons(object target)
        {
            foreach (MethodInfo method in target.GetType().GetMethods())
            {
                var attribute = method.GetCustomAttribute<ImGUIButtonAttribute>();
                if (attribute != null)
                    if (ImGui.Button(attribute.buttonText))
                    {
                        method.Invoke(target, null);
                        return true;
                    }
            }
            return false;
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class HideInInspectorAttribute : Attribute
    {
    }
}
