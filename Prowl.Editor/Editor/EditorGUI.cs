using Microsoft.VisualBasic.FileIO;
using Prowl.Editor.Assets;
using Prowl.Editor.PropertyDrawers;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using System.Reflection;
using System.Runtime.CompilerServices;
using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor
{
    public static class EditorGUI
    {
        public static void Separator(float thickness = 1f, [CallerLineNumber] int line = 0)
        {
            var g = ActiveGUI;
            using (g.Node("Seperator", line).ExpandWidth().Height(GuiStyle.ItemHeight / 2).Enter())
            {
                // Draw Line in middle of rect
                var start = g.CurrentNode.LayoutData.GlobalContentPosition;
                start.y += g.CurrentNode.LayoutData.Rect.height / 2;
                var end = start + new Vector2(g.CurrentNode.LayoutData.Rect.width, 0);
                g.Draw2D.DrawLine(start, end, GuiStyle.Borders, thickness);
            }
        }

        public static void Text(string text, [CallerLineNumber] int line = 0)
        {
            var g = ActiveGUI;
            using (g.Node(text, line).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                g.Draw2D.DrawText(text, g.CurrentNode.LayoutData.Rect, GuiStyle.Base11);
            }
        }

        public static bool StyledButton(string label)
        {
            var g = ActiveGUI;
            using (g.Node(label).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 10);

                g.Draw2D.DrawText(label, g.CurrentNode.LayoutData.Rect, GuiStyle.Base8);
                
                if (g.IsNodePressed())
                {
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 10);
                    return true;
                }

                if (g.IsNodeHovered())
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5 * 0.5f, 10);


                return false;
            }
        }

        public static bool StyledButton(string label, double width, double height, bool border = true, Color? textcolor = null)
        {
            var g = ActiveGUI;
            using (g.Node(label).Width(width).Height(height).Enter())
            {
                if(border)
                    g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 10);

                g.Draw2D.DrawText(label, g.CurrentNode.LayoutData.Rect, textcolor ?? GuiStyle.Base11);
                
                if (g.IsNodePressed())
                {
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 10);
                    return true;
                }

                var hovCol = GuiStyle.Base11;
                hovCol.a = 0.25f;
                if (g.IsNodeHovered())
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, hovCol, 10);


                return false;
            }
        }

        public static bool InputDouble(string ID, ref double value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = value.ToString();
            var changed = ActiveGUI.InputField(ID, ref textValue, 255, InputFieldFlags.NumbersOnly, x, y, width, null, style);
            if (changed && Double.TryParse(textValue, out value)) return true;
            return false;
        }
        public static bool InputFloat(string ID, ref float value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = value.ToString();
            var changed = ActiveGUI.InputField(ID, ref textValue, 255, InputFieldFlags.NumbersOnly, x, y, width, null, style);
            if (changed && float.TryParse(textValue, out value)) return true;
            return false;
        }

        public static bool InputLong(string ID, ref long value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = value.ToString();
            var changed = ActiveGUI.InputField(ID, ref textValue, 255, InputFieldFlags.NumbersOnly, x, y, width, null, style);
            if (changed && long.TryParse(textValue, out value)) return true;
            return false;
        }


        public static GuiStyle VectorXStyle = new GuiStyle() { TextColor = GuiStyle.Red, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };
        public static GuiStyle VectorYStyle = new GuiStyle() { TextColor = GuiStyle.Emerald, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };
        public static GuiStyle VectorZStyle = new GuiStyle() { TextColor = GuiStyle.Blue, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };
         
        public static GuiStyle InputFieldStyle = new GuiStyle() { TextColor = GuiStyle.Emerald, WidgetColor = GuiStyle.WindowBackground, Border = GuiStyle.Borders, BorderThickness = 1 };


        #region PropertyGrid
        public enum TargetFields
        {
            Serializable,
            Public,
            Private,
            All
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

        public static bool PropertyGrid(string name, ref object target, TargetFields targetFields, PropertyGridConfig config = PropertyGridConfig.None)
        {
            bool changed = false;
            if (target == null) return changed;

            LayoutNode node;
            using ((node = ActiveGUI.Node(name)).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Spacing(5).Enter())
            {
                // Draw the Background & Borders
                if (!config.HasFlag(PropertyGridConfig.NoBackground))
                    ActiveGUI.Draw2D.DrawRectFilled(node.LayoutData.Rect, GuiStyle.FrameBGColor);

                if (!config.HasFlag(PropertyGridConfig.NoBorder))
                    ActiveGUI.Draw2D.DrawRect(node.LayoutData.Rect, GuiStyle.Borders);

                if (!config.HasFlag(PropertyGridConfig.NoHeader))
                {
                    node.PaddingTop(GuiStyle.ItemHeight);

                    // Draw the header
                    var headerRect = new Rect(node.LayoutData.GlobalPosition, new Vector2(node.LayoutData.Rect.width, GuiStyle.ItemHeight));
                    if (!config.HasFlag(PropertyGridConfig.NoBackground))
                        ActiveGUI.Draw2D.DrawRectFilled(headerRect, GuiStyle.Indigo);

                    ActiveGUI.Draw2D.DrawText(UIDrawList.DefaultFont, name, 20, headerRect, GuiStyle.Base11, false);
                }

                changed = DrawProperties(ref target, targetFields, config);

            }


            return changed;
        }

        private static bool DrawProperties(ref object target, TargetFields targetFields, PropertyGridConfig config = PropertyGridConfig.None)
        {
            // Get the target fields
            var fields = targetFields switch {
                TargetFields.Serializable => RuntimeUtils.GetSerializableFields(target),
                TargetFields.Public => target.GetType().GetFields(),
                TargetFields.Private => target.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
                _ => target.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            };

            // Draw the properties
            bool changed = false;
            int i = 0;
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;
                var fieldValue = field.GetValue(target);

                // if has HideInInspector ignore
                if (field.GetCustomAttributes(typeof(HideInInspectorAttribute), true).Length > 0)
                    continue;

                // Draw the property
                changed |= DrawerAttribute.DrawProperty(ActiveGUI, field.Name, i++, fieldType, ref fieldValue, config);

                // Update the value
                field.SetValue(target, fieldValue);
            }

            return changed;
        }

        public static bool DrawProperty<T>(int index, string name, ref T? value, PropertyGridConfig config = PropertyGridConfig.None)
        {
            object? obj = value;
            bool changed = DrawerAttribute.DrawProperty(ActiveGUI, name, index, typeof(T), ref obj, config);
            value = (T?)obj;
            return changed;
        }

        #endregion

    }
}