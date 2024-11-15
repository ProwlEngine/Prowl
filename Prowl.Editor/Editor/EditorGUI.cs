// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;
using System.Runtime.CompilerServices;

using Prowl.Editor.Preferences;
using Prowl.Editor.PropertyDrawers;
using Prowl.Editor.Utilities;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.Utils;

using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor;

public static class EditorGUI
{

    static double ItemSize => EditorStylePrefs.Instance.ItemSize;

    public static void Separator(float thickness = 1f, [CallerLineNumber] int line = 0)
    {
        var g = ActiveGUI;
        using (g.Node("Seperator", line).ExpandWidth().Height(ItemSize / 2).Enter())
        {
            // Draw Line in middle of rect
            var start = g.CurrentNode.LayoutData.GlobalContentPosition;
            start.y += g.CurrentNode.LayoutData.Rect.height / 2;
            var end = start + new Vector2(g.CurrentNode.LayoutData.Rect.width, 0);
            g.Draw2D.DrawLine(start, end, EditorStylePrefs.Instance.Borders, thickness);
        }
    }

    public static void Text(string text, [CallerLineNumber] int line = 0, bool doWrap = true)
    {
        var g = ActiveGUI;
        var height = Font.DefaultFont.CalcTextSize(text, 0, doWrap ? g.CurrentNode.LayoutData.InnerRect.width : -1).y;
        using (g.Node(text, line).ExpandWidth().Height(height).Enter())
        {
            //g.Draw2D.DrawText(text, g.CurrentNode.LayoutData.Rect);

            var rect = g.CurrentNode.LayoutData.InnerRect;
            var pos = new Vector2(rect.x, rect.y);
            var wrap = rect.width;
            var textSize = Font.DefaultFont.CalcTextSize(text, 20, 0, doWrap ? wrap : -1);
            pos.x += MathD.Max((rect.width - textSize.x) * 0.5f, 0.0);
            pos.y += (rect.height - textSize.y) * 0.5f;
            g.Draw2D.DrawText(Font.DefaultFont, text, 20, pos, Color.white, doWrap ? wrap : 0, rect);
        }
    }

    public static void TextSimple(string text, [CallerLineNumber] int line = 0, bool doWrap = true)
    {
        var g = ActiveGUI;
        var height = Font.DefaultFont.CalcTextSize(text, 0, doWrap ? g.CurrentNode.LayoutData.InnerRect.width : -1).y;
        using (g.Node(text, line).ExpandWidth().Height(height).Enter())
        {
            //g.Draw2D.DrawText(text, g.CurrentNode.LayoutData.Rect);

            var rect = g.CurrentNode.LayoutData.InnerRect;
            var pos = new Vector2(rect.x, rect.y);
            var wrap = rect.width;
            g.Draw2D.DrawText(Font.DefaultFont, text, 20, pos, Color.white, doWrap ? wrap : 0, rect);
        }
    }

    public static bool StyledButton(string label)
    {
        var g = ActiveGUI;
        using (g.Node(label).ExpandWidth().Height(ItemSize).Enter())
        {
            g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1, (float)EditorStylePrefs.Instance.ButtonRoundness);

            if (g.IsNodePressed())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.ButtonRoundness);
                return true;
            }

            if (g.IsNodeHovered())
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

            g.Draw2D.DrawText(label, g.CurrentNode.LayoutData.Rect);

            return false;
        }
    }

    public static bool StyledButton(string label, double width, double height, bool border = true, Color? textcolor = null, Color? bgcolor = null, float? roundness = null, string tooltip = "")
    {
        roundness ??= (float)EditorStylePrefs.Instance.ButtonRoundness;
        var g = ActiveGUI;
        using (g.Node(label).Width(width).Height(height).Enter())
        {
            if (border)
                g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1, roundness.Value);
            if (bgcolor != null)
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, bgcolor.Value, roundness.Value);

            if (g.IsNodePressed())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, roundness.Value);
                return true;
            }

            if (g.IsNodeHovered())
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, roundness.Value);

            g.Draw2D.DrawText(label, g.CurrentNode.LayoutData.Rect, textcolor ?? Color.white);
            g.Tooltip(tooltip);

            return false;
        }
    }


    public static WidgetStyle VectorXStyle => new WidgetStyle((float)EditorStylePrefs.Instance.ItemSize)
    {
        TextColor = EditorStylePrefs.Red,
        BGColor = EditorStylePrefs.Instance.WindowBGOne,
        BorderColor = EditorStylePrefs.Instance.Borders,
        BorderThickness = 1,
        Roundness = (float)EditorStylePrefs.Instance.ButtonRoundness
    };

    public static WidgetStyle VectorYStyle => new WidgetStyle((float)EditorStylePrefs.Instance.ItemSize)
    {
        TextColor = EditorStylePrefs.Emerald,
        BGColor = EditorStylePrefs.Instance.WindowBGOne,
        BorderColor = EditorStylePrefs.Instance.Borders,
        BorderThickness = 1,
        Roundness = (float)EditorStylePrefs.Instance.ButtonRoundness
    };

    public static WidgetStyle VectorZStyle => new WidgetStyle((float)EditorStylePrefs.Instance.ItemSize)
    {
        TextColor = EditorStylePrefs.Blue,
        BGColor = EditorStylePrefs.Instance.WindowBGOne,
        BorderColor = EditorStylePrefs.Instance.Borders,
        BorderThickness = 1,
        Roundness = (float)EditorStylePrefs.Instance.ButtonRoundness
    };

    public static WidgetStyle InputStyle => new WidgetStyle((float)EditorStylePrefs.Instance.ItemSize)
    {
        TextColor = EditorStylePrefs.Instance.LesserText,
        ActiveColor = EditorStylePrefs.Instance.Highlighted,
        HoveredColor = EditorStylePrefs.Instance.Hovering,
        BGColor = EditorStylePrefs.Instance.WindowBGOne,
        BorderColor = EditorStylePrefs.Instance.Borders,
        BorderThickness = 1,
        Roundness = (float)EditorStylePrefs.Instance.ButtonRoundness
    };

    public static WidgetStyle InputFieldStyle => new WidgetStyle((float)EditorStylePrefs.Instance.ItemSize)
    {
        TextColor = EditorStylePrefs.Emerald,
        BGColor = EditorStylePrefs.Instance.WindowBGOne,
        BorderColor = EditorStylePrefs.Instance.Borders,
        BorderThickness = 1,
        Roundness = (float)EditorStylePrefs.Instance.ButtonRoundness
    };


    #region PropertyGrid
    [Flags]
    public enum TargetFields
    {
        Serializable = 0,
        Properties = 1,
        Public = 2,
        Private = 4,
        All = 8
    }

    [Flags]
    public enum PropertyGridConfig
    {
        None = 0,
        NoHeader = 1,
        NoLabel = 2,
        NoTooltip = 4,
        NoBorder = 8,
        NoBackground = 16,
        NoCollapse = 64,

        Debug = 128
    }

    public class FieldChanges
    {
        public List<(object target, FieldInfo field)> AllChanges = [];

        public void Add(object obj, string name) => AllChanges.Add((obj, obj.GetType().GetField(name)));
        public void Add(object obj, FieldInfo field) => AllChanges.Add((obj, field));
    }


    public static bool PropertyGrid(string name, ref object target, TargetFields targetFields, PropertyGridConfig config = PropertyGridConfig.None, FieldChanges? changes = null)
    {
        bool changed = false;
        if (target == null) return changed;

        LayoutNode node;
        using ((node = ActiveGUI.Node(name)).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Spacing(5).Enter())
        {
            DrawGrid(name, config, node);

            changed = DrawProperties(ref target, targetFields, config, changes);
        }

        return changed;
    }

    public static bool PropertyGrid(string name, ref object target, List<MemberInfo> members, PropertyGridConfig config = PropertyGridConfig.None, FieldChanges? changes = null)
    {
        bool changed = false;
        if (target == null) return changed;

        LayoutNode node;
        using ((node = ActiveGUI.Node(name)).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Spacing(5).Enter())
        {
            DrawGrid(name, config, node);

            changed = DrawProperties(ref target, members, config, changes);
        }

        return changed;
    }

    private static void DrawGrid(string name, PropertyGridConfig config, LayoutNode node)
    {

        // Draw the Background & Borders
        if (!config.HasFlag(PropertyGridConfig.NoBackground))
            ActiveGUI.Draw2D.DrawRectFilled(node.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne);

        if (!config.HasFlag(PropertyGridConfig.NoBorder))
            ActiveGUI.Draw2D.DrawRect(node.LayoutData.Rect, EditorStylePrefs.Instance.Borders);

        if (!config.HasFlag(PropertyGridConfig.NoHeader))
        {
            node.PaddingTop(ItemSize);

            // Draw the header
            var headerRect = new Rect(node.LayoutData.GlobalPosition, new Vector2(node.LayoutData.Rect.width, ItemSize));
            if (!config.HasFlag(PropertyGridConfig.NoBackground))
                ActiveGUI.Draw2D.DrawRectFilled(headerRect, EditorStylePrefs.Indigo);

            ActiveGUI.Draw2D.DrawText(RuntimeUtils.Prettify(name), headerRect, false);
        }
    }

    private static bool DrawProperties(ref object target, TargetFields targetFields, PropertyGridConfig config = PropertyGridConfig.None, FieldChanges? changes = null)
    {
        // Get the target fields
        List<MemberInfo> members = [];

        bool all = targetFields.HasFlag(TargetFields.All);

        if (all || targetFields.HasFlag(TargetFields.Serializable))
            members.AddRange(target.GetSerializableFields());

        if (all || targetFields.HasFlag(TargetFields.Public))
            members.AddRange(target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance));

        if (all || targetFields.HasFlag(TargetFields.Private))
            members.AddRange(target.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance));

        if (all || targetFields.HasFlag(TargetFields.Properties))
            members.AddRange(target.GetType().GetProperties().Where(prop => prop.CanRead && prop.CanWrite && prop.GetCustomAttribute<ShowInInspectorAttribute>(false) != null));

        return DrawProperties(ref target, members, config, changes);
    }

    private static bool DrawProperties(ref object target, List<MemberInfo> members, PropertyGridConfig config = PropertyGridConfig.None, FieldChanges? changes = null)
    {
        // Draw the fields & properties
        bool changed = false;
        int i = 0;
        foreach (var field in members.Distinct())
        {
            // if has HideInInspector ignore
            if (field.GetCustomAttributes(typeof(HideInInspectorAttribute), true).Length > 0)
                continue;

            var attributes = field.GetCustomAttributes(true);
            var imGuiAttributes = attributes.Where(attr => attr is InspectorUIAttribute).Cast<InspectorUIAttribute>();
            if (!HandleBeginGUIAttributes("attrib" + i, target, imGuiAttributes))
                continue;

            var fieldType = field is FieldInfo ? (field as FieldInfo)!.FieldType : (field as PropertyInfo)!.PropertyType;
            var fieldValue = field.GetValue(target);

            bool isPrefabModified = false;
            if (target is MonoBehaviour comp)
                isPrefabModified = comp.GameObject.AffectedByPrefabLink != null && comp.GameObject.AffectedByPrefabLink.HasChange(comp, field.Name);

            // Draw the property
            bool propChange = DrawerAttribute.DrawProperty(ActiveGUI, field.Name + (isPrefabModified ? "*" : ""), i++, fieldType, ref fieldValue, config);

            HandleEndAttributes(imGuiAttributes);

            // Update the value
            if (propChange)
            {
                UndoRedoManager.SetMember(target, field, fieldValue);

                if (changes != null && field is FieldInfo f)
                    changes.Add(target, f);
            }

            changed |= propChange;
        }

        changed |= HandleAttributeButtons("Btn", target);

        return changed;
    }

    public static bool DrawPropertyNoUndo<T>(int index, string name, ref T? value, PropertyGridConfig config = PropertyGridConfig.None)
    {
        object? obj = value;
        bool changed = DrawerAttribute.DrawProperty(ActiveGUI, name, index, typeof(T), ref obj, config);
        value = (T?)obj;
        return changed;
    }

    public static bool DrawProperty(int index, string name, object target, string field, PropertyGridConfig config = PropertyGridConfig.None)
    {
        FieldInfo? fieldInfo = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fieldInfo != null)
        {
            object? obj = fieldInfo.GetValue(target);
            bool changed = DrawerAttribute.DrawProperty(ActiveGUI, name, index, fieldInfo.FieldType, ref obj, config);
            if (changed)
                UndoRedoManager.SetMember(target, fieldInfo, obj);
            return changed;
        }

        PropertyInfo? propInfo = target.GetType().GetProperty(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (propInfo != null)
        {
            object? obj = propInfo.GetValue(target);
            bool changed = DrawerAttribute.DrawProperty(ActiveGUI, name, index, propInfo.PropertyType, ref obj, config);
            if (changed)
                UndoRedoManager.SetMember(target, propInfo, obj);
            return changed;
        }

        return false;
    }

    static bool HandleBeginGUIAttributes(string id, object target, IEnumerable<InspectorUIAttribute> attribs)
    {
        foreach (InspectorUIAttribute guiAttribute in attribs)
        {
            switch (guiAttribute.AttribType())
            {
                case GuiAttribType.Space:
                    // Dummy node
                    ActiveGUI.Node("Space" + id, guiAttribute.GetHashCode()).ExpandWidth().Height(ItemSize);
                    break;

                case GuiAttribType.Text:
                    var text = guiAttribute as TextAttribute;
                    ActiveGUI.TextNode("Label" + id, text.text).ExpandWidth().Height(ItemSize);
                    break;

                case GuiAttribType.ShowIf:
                    var showIf = guiAttribute as ShowIfAttribute;
                    var field = target.GetType().GetField(showIf.propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        if ((bool)field.GetValue(target) == showIf.inverted)
                            return false;
                    }
                    else
                    {
                        var prop = target.GetType().GetProperty(showIf.propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (prop != null && prop.PropertyType == typeof(bool))
                        {
                            if ((bool)prop.GetValue(target) == showIf.inverted)
                                return false;
                        }
                    }
                    break;

                case GuiAttribType.Separator:
                    Separator(1, id.GetHashCode());
                    break;

            }
        }
        return true;
    }

    static void HandleEndAttributes(IEnumerable<InspectorUIAttribute> attribs)
    {
        foreach (InspectorUIAttribute guiAttribute in attribs)
            switch (guiAttribute.AttribType())
            {
                case GuiAttribType.Tooltip:
                    var tooltip = guiAttribute as TooltipAttribute;
                    ActiveGUI.Tooltip(tooltip.tooltip);
                    break;

            }
    }

    public static bool HandleAttributeButtons(string id, object target)
    {
        int count = 0;
        foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
        {
            count++;
            var attribute = method.GetCustomAttribute<GUIButtonAttribute>();
            if (attribute != null)
                using (ActiveGUI.Node("button" + id, count).ExpandWidth().Height(ItemSize).Enter())
                {
                    if (ActiveGUI.IsNodePressed())
                    {
                        try
                        {
                            method.Invoke(target, null);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("Error During ImGui Button Execution: " + e.Message + "\n" + e.StackTrace);
                        }
                        ActiveGUI.Draw2D.DrawRectFilled(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.ButtonRoundness);
                        return true;
                    }
                    else if (ActiveGUI.IsNodeHovered())
                        ActiveGUI.Draw2D.DrawRectFilled(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);
                    else
                        ActiveGUI.Draw2D.DrawRect(ActiveGUI.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1, (float)EditorStylePrefs.Instance.ButtonRoundness);

                    ActiveGUI.Draw2D.DrawText(attribute.buttonText, ActiveGUI.CurrentNode.LayoutData.Rect);
                }
        }
        return false;
    }

    #endregion

}
