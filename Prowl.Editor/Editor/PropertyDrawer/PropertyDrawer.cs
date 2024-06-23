using Prowl.Editor.Utilities;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;
using System.Reflection;

namespace Prowl.Editor.PropertyDrawers
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class DrawerAttribute(Type type) : Attribute
    {
        public Type TargetType { get; private set; } = type;

        private static Dictionary<Type, PropertyDrawer> drawers = [];

        public static bool DrawProperty(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config)
        {
            if (drawers.TryGetValue(propertyType, out var drawer))
                return drawer.PropertyLayout(gui, label, index, propertyType, ref propertyValue, config);

            // No direct drawer found, try to find a drawer for the base type
            foreach (var kvp in drawers)
                if (propertyType.IsAssignableTo(kvp.Key))
                    return kvp.Value.PropertyLayout(gui, label, index, propertyType, ref propertyValue, config);

            if (propertyType.IsInterface || propertyType.IsAbstract)
            {
                // its an Interface/Abstract class, 
            }

            // No drawer found, Fallback to Default Drawer
            bool changed = false;
            var fields = RuntimeUtils.GetSerializableFields(propertyValue);
            if (fields.Length != 0)
            {
                using (gui.Node(label + "_Header", index).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Padding(10).Enter())
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Background);

                    gui.TextNode("H_Text", label).ExpandWidth().Height(GuiStyle.ItemHeight);

                    changed |= EditorGUI.PropertyGrid(propertyType.Name + " | " + label, ref propertyValue, EditorGUI.TargetFields.Serializable, config);
                }
            }
            return changed;
        }

        [OnAssemblyUnload]
        public static void ClearDrawers()
        {
            drawers.Clear();
        }

        [OnAssemblyLoad]
        public static void FindAllDrawers()
        {
            foreach (Assembly editorAssembly in AssemblyManager.ExternalAssemblies.Append(typeof(Program).Assembly))
            {
                List<Type> derivedTypes = EditorUtils.GetDerivedTypes(typeof(PropertyDrawer), editorAssembly);
                foreach (var type in derivedTypes)
                {
                    var attribute = type.GetCustomAttribute<DrawerAttribute>();
                    if (attribute != null)
                    {
                        PropertyDrawer drawer = (PropertyDrawer)Activator.CreateInstance(type);
                        if (drawer == null)
                        {
                            Debug.LogError($"Failed to create instance of {type.Name}");
                            continue;
                        }
                        if (!drawers.TryAdd(attribute.TargetType, drawer))
                        {
                            Debug.LogError($"Failed to add PropertyDrawer for {attribute.TargetType.Name}, already exists?");
                            continue;
                        }
                    }
                }
            }
        }
    }

    public abstract class PropertyDrawer
    {
        public virtual bool PropertyLayout(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config)
        {
            using (gui.Node(label, index).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Draw line down the middle
                //var start = new Vector2(ActiveGUI.CurrentNode.LayoutData.Rect.x + ActiveGUI.CurrentNode.LayoutData.Rect.width / 2, ActiveGUI.CurrentNode.LayoutData.Rect.y);
                //var end = new Vector2(start.x, ActiveGUI.CurrentNode.LayoutData.Rect.y + ActiveGUI.CurrentNode.LayoutData.Rect.height);
                //ActiveGUI.DrawLine(start, end, GuiStyle.Borders);

                // Label
                if (!config.HasFlag(EditorGUI.PropertyGridConfig.NoLabel))
                    OnLabelGUI(gui, label);

                // Value
                using (gui.Node("#_Value").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                    return OnValueGUI(gui, $"#{label}_{index}", propertyType, ref propertyValue);
            }
        }

        public virtual void OnLabelGUI(Gui gui, string label)
        {
            using (gui.Node("#_Label").ExpandHeight().Clip().Enter())
            {
                var pos = gui.CurrentNode.LayoutData.Rect.Min;
                pos.x += 28;
                pos.y += 5;
                string pretty = RuntimeUtils.Prettify(label);
                gui.Draw2D.DrawText(pretty, pos, GuiStyle.Base8);
            }
        }

        public virtual bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
        {
            var col = GuiStyle.Red * (gui.IsNodeHovered() ? 1f : 0.8f);
            var pos = gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(0, 8);
            gui.Draw2D.DrawText(targetValue.ToString(), pos, col);
            return false;
        }
    }
}
