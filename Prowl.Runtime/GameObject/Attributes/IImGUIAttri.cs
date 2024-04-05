using System;
using T = System.AttributeTargets;

namespace Prowl.Runtime
{
    public enum GuiAttribType { Space, Text, Separator, Sameline, Disabled, Header, StartGroup, EndGroup, Indent, Unindent, ShowIf, Tooltip, Button }

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
    public class TextAttribute(string text) : Attribute, IImGUIAttri
    {
        public string text = text;
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
    public class HeaderAttribute(string name) : Attribute, IImGUIAttri
    {
        public string name = name;
        public GuiAttribType AttribType() => GuiAttribType.Header;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class StartGroupAttribute(string name, float height = 100f, float headerSize = 1f, bool collapsable = true) : Attribute, IImGUIAttri
    {
        public string name = name;
        public float height = height;
        public float headerSize = headerSize;
        public bool collapsable = collapsable;
        public GuiAttribType AttribType() => GuiAttribType.StartGroup;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class EndGroupAttribute : Attribute, IImGUIAttri
    {
        public GuiAttribType AttribType() => GuiAttribType.EndGroup;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class TooltipAttribute(string text) : Attribute, IImGUIAttri
    {
        public string tooltip = text;
        public GuiAttribType AttribType() => GuiAttribType.Tooltip;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class ShowIfAttribute(string propertyName, bool inverted = false) : Attribute, IImGUIAttri
    {
        public string propertyName = propertyName;
        public bool inverted = inverted;
        public GuiAttribType AttribType() => GuiAttribType.ShowIf;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class IndentAttribute(int indent = 4) : Attribute, IImGUIAttri
    {
        public int indent = indent;
        public GuiAttribType AttribType() => GuiAttribType.Indent;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class UnindentAttribute(int unindent = 4) : Attribute, IImGUIAttri
    {
        public int unindent = unindent;
        public GuiAttribType AttribType() => GuiAttribType.Unindent;
    }

    [AttributeUsage(T.Method, AllowMultiple = false)]
    public class ImGUIButtonAttribute(string text) : Attribute 
    { 
        public string buttonText = text;
    }

    [AttributeUsage(T.Field, AllowMultiple = false)]
    public class HideInInspectorAttribute : Attribute { }
}
