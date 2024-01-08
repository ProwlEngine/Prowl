using System;
using T = System.AttributeTargets;

namespace Prowl.Runtime
{
    public enum GuiAttribType { Space, Text, Separator, Sameline, Disabled, Header, StartGroup, EndGroup, Tooltip, Button }

    public interface IImGUIAttri
    {
        public GuiAttribType AttribType();
    }

    [AttributeUsage(T.Field, AllowMultiple = true)]
    public class SpaceAttribute : Attribute, IImGUIAttri
    {
        public GuiAttribType AttribType() => GuiAttribType.Space;
    }

    [AttributeUsage(T.Field, AllowMultiple = true)]
    public class TextAttribute : Attribute, IImGUIAttri
    {
        public string text;
        public TextAttribute(string text) { this.text = text; }
        public GuiAttribType AttribType() => GuiAttribType.Text;
    }

    [AttributeUsage(T.Field, AllowMultiple = true)]
    public class SeparatorAttribute : Attribute, IImGUIAttri
    {
        public GuiAttribType AttribType() => GuiAttribType.Separator;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class SameLineAttribute : Attribute, IImGUIAttri
    {
        public GuiAttribType AttribType() => GuiAttribType.Sameline;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class DisabledAttribute : Attribute, IImGUIAttri
    {
        public GuiAttribType AttribType() => GuiAttribType.Disabled;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class HeaderAttribute : Attribute, IImGUIAttri
    {
        public string name;
        public HeaderAttribute(string name) { this.name = name; }
        public GuiAttribType AttribType() => GuiAttribType.Header;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class StartGroupAttribute : Attribute, IImGUIAttri
    {
        public string name;
        public float height;
        public float headerSize;
        public bool collapsable;

        public StartGroupAttribute(string name, float height = 100f, float headerSize = 1f, bool collapsable = true)
        {
            this.name = name;
            this.height = height;
            this.headerSize = headerSize;
            this.collapsable = collapsable;
        }

        public GuiAttribType AttribType() => GuiAttribType.StartGroup;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class EndGroupAttribute : Attribute, IImGUIAttri
    {
        public GuiAttribType AttribType() => GuiAttribType.EndGroup;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class TooltipAttribute : Attribute, IImGUIAttri
    {
        public string tooltip;
        public TooltipAttribute(string text) => tooltip = text;
        public GuiAttribType AttribType() => GuiAttribType.Tooltip;
    }

    [AttributeUsage(T.Method, AllowMultiple = false)]
    public class ImGUIButtonAttribute : Attribute 
    { 
        public string buttonText;
        public ImGUIButtonAttribute(string text) => buttonText = text;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class HideInInspectorAttribute : Attribute { }
}
