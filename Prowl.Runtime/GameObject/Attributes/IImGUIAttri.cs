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
    public class TextAttribute : Attribute, IImGUIAttri
    {
        string text;
        public TextAttribute(string text) { this.text = text; }
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
    public class StartGroupAttribute : Attribute, IImGUIAttri
    {
        string name;
        float height;
        float headerSize;
        public StartGroupAttribute(string name, float height = 100f, float headerSize = 1f) { this.name = name; this.height = height; this.headerSize = headerSize; }
        public void Draw()
        {
            GUIHelper.TextCenter(name, headerSize);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2);
            ImGui.BeginChild(name, new System.Numerics.Vector2(-1, height), true, ImGuiWindowFlags.NoCollapse);
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
    public class TooltipAttribute : Attribute, IImGUIAttri
    {
        string tooltip;
        public TooltipAttribute(string tooltip) { this.tooltip = tooltip; }
        public void Draw() { }
        public void End() => GUIHelper.Tooltip(tooltip);
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ImGUIButtonAttribute : Attribute
    {
        internal string buttonText;
        public ImGUIButtonAttribute(string buttonText) { this.buttonText = buttonText; }
        public static void DrawButtons(object target)
        {
            foreach (MethodInfo method in target.GetType().GetMethods())
            {
                var attribute = method.GetCustomAttribute<ImGUIButtonAttribute>();
                if (attribute != null)
                    if (ImGui.Button(attribute.buttonText))
                        method.Invoke(target, null);
            }
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class HideInInspectorAttribute : Attribute
    {
    }
}
