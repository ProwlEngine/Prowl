// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Preferences;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.PropertyDrawers;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class DrawerAttribute(Type type) : Attribute
{
    public Type TargetType { get; private set; } = type;

    private static readonly Dictionary<Type, PropertyDrawer> knownDrawers = [];

    private static readonly Dictionary<Type, PropertyDrawer?> cachedDrawers = [];

    private static List<Type> implementationTypes = [];

    public static bool DrawProperty(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config)
    {
        bool changed = false;
        if (cachedDrawers.TryGetValue(propertyType, out var cached))
        {
            if (cached != null)
                return cached.PropertyLayout(gui, label, index, propertyType, ref propertyValue, config);

            FallbackDrawer(gui, label, index, propertyType, ref propertyValue, config, ref changed);
            return changed;
        }
        else
        {
            // Interfaces and Abstract classes need a drawer for them that override other drawers
            if (propertyType.IsInterface || propertyType.IsAbstract)
            {
                InterfaceDrawer(gui, label, index, propertyType, ref propertyValue, config, ref changed);
                return changed;
            }

            // Check if we have a drawer for the type
            if (knownDrawers.TryGetValue(propertyType, out var drawer))
            {
                cachedDrawers[propertyType] = drawer;
                return drawer.PropertyLayout(gui, label, index, propertyType, ref propertyValue, config);
            }

            // No direct drawer found, try to find a drawer for the base type
            foreach (var kvp in knownDrawers)
                if (propertyType.IsAssignableTo(kvp.Key))
                {
                    cachedDrawers[propertyType] = kvp.Value;
                    return kvp.Value.PropertyLayout(gui, label, index, propertyType, ref propertyValue, config);
                }


            // No drawer found, Fallback to Default Drawer
            cachedDrawers[propertyType] = null;
            FallbackDrawer(gui, label, index, propertyType, ref propertyValue, config, ref changed);
            return changed;
        }
    }

    private static void FallbackDrawer(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config, ref bool changed)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;
        if (propertyValue == null)
        {
            // Null Drawer
            using (gui.Node(label + "_Null", index).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                using (gui.Node("Creator", index).MaxWidth(ItemSize).Height(ItemSize).Layout(LayoutType.Row).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        propertyValue = Activator.CreateInstance(propertyType);
                        changed = true;
                    }
                    else if (gui.IsNodeHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

                    gui.Draw2D.DrawText(FontAwesome6.Plus, 20, gui.CurrentNode.LayoutData.InnerRect, Color.white);
                }

                using (gui.Node("Label", index).Height(ItemSize).Layout(LayoutType.Row).Enter())
                {
                    gui.Draw2D.DrawText("(Null)", 20, gui.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.Warning);
                }
            }
            return;
        }

        var fields = propertyValue.GetSerializableFields();
        if (fields.Length != 0)
        {
            using (gui.Node(label + "_Header", index).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                if (!config.HasFlag(EditorGUI.PropertyGridConfig.NoBackground))
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness);

                gui.TextNode("H_Text", RuntimeUtils.Prettify(label)).ExpandWidth().Height(ItemSize).IgnoreLayout();

                bool enumexpanded = gui.GetNodeStorage("enumexpanded", false);
                using (gui.Node("EnumExpandBtn").TopLeft(5, 0).Scale(ItemSize).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        enumexpanded = !enumexpanded;
                        gui.SetNodeStorage(gui.CurrentNode.Parent, "enumexpanded", enumexpanded);
                    }
                    gui.Draw2D.DrawText(enumexpanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, gui.CurrentNode.LayoutData.InnerRect, gui.IsNodeHovered() ? EditorStylePrefs.Instance.LesserText : Color.white);
                }

                float scaleAnimContent = gui.AnimateBool(enumexpanded, 0.15f, EaseType.Linear);
                if (enumexpanded || scaleAnimContent > 0)
                    using (gui.Node("PropertyGridHolder").ExpandWidth().FitContentHeight(scaleAnimContent).Enter())
                        changed |= EditorGUI.PropertyGrid(propertyType.Name + " | " + label, ref propertyValue, EditorGUI.TargetFields.Serializable | EditorGUI.TargetFields.Properties, config);
            }
        }
    }

    private static void InterfaceDrawer(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config, ref bool changed)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        using (gui.Node(label + "_Interface", index).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
        {
            using (gui.Node("Creator", index).MaxWidth(ItemSize).Height(ItemSize).Layout(LayoutType.Row).Enter())
            {
                if (gui.IsNodePressed())
                {
                    gui.OpenPopup("Create_Interface");

                    // Ignore generics since we can't instantiate them
                    implementationTypes = RuntimeUtils.FindTypesImplementing(propertyType, true);
                }
                else if (gui.IsNodeHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

                gui.Draw2D.DrawText(FontAwesome6.Plus, 20, gui.CurrentNode.LayoutData.InnerRect, Color.white);


                if (gui.BeginPopup("Create_Interface", out var popupNode))
                    using (popupNode.Width(200).FitContentHeight().Layout(LayoutType.Column).Padding(5).Enter())
                    {
                        foreach (var t in implementationTypes)
                        {
                            if (EditorGUI.StyledButton(t.Name))
                            {
                                propertyValue = Activator.CreateInstance(t);
                                changed = true;
                                gui.CloseAllPopups();
                            }
                        }
                    }

            }

            using (gui.Node("Value", index).Height(ItemSize).Layout(LayoutType.Row).Enter())
            {
                gui.CurrentNode.Layout(LayoutType.Row).ScaleChildren();

                gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1, 2);

                bool pressed = gui.IsNodePressed(); // Lets UI know this node can take focus

                var pos = gui.CurrentNode.LayoutData.GlobalContentPosition;
                var centerY = (gui.CurrentNode.LayoutData.InnerRect.height / 2) - (20 / 2);
                pos += new Vector2(5, centerY + 3);
                if (propertyValue == null)
                {
                    string text = "(Null) " + propertyType.Name;
                    var col = EditorStylePrefs.Red * (gui.IsNodeHovered() ? 1f : 0.8f);
                    gui.Draw2D.DrawText(text, pos, col);
                }
                else
                {
                    gui.Draw2D.DrawText(propertyValue.ToString(), pos, Color.white * (gui.IsNodeHovered() ? 1f : 0.8f));
                    if (gui.IsNodeHovered() && gui.IsPointerDoubleClick(MouseButton.Left))
                        GlobalSelectHandler.Select(propertyValue);
                }

                // Drag and drop support
                if (DragnDrop.Drop(out object? instance, propertyType))
                {
                    propertyValue = instance;
                    changed = true;
                }

                // support looking for components on dropped GameObjects
                if (DragnDrop.Drop(out GameObject go))
                {
                    propertyValue = go.GetComponent(propertyType);
                    changed = true;
                }

                if (gui.IsNodeActive() || gui.IsNodeFocused())
                {
                    gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, 1, (float)EditorStylePrefs.Instance.ButtonRoundness);

                    if (gui.IsKeyDown(Key.Delete))
                    {
                        propertyValue = null;
                        changed = true;
                    }
                }
            }
        }
    }

    [OnAssemblyUnload]
    public static void ClearDrawers()
    {
        knownDrawers.Clear();
        cachedDrawers.Clear();
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
                    if (!knownDrawers.TryAdd(attribute.TargetType, drawer))
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
    public virtual double MinWidth => 75;

    public virtual bool PropertyLayout(Gui gui, string label, int index, Type propertyType, ref object? propertyValue, EditorGUI.PropertyGridConfig config)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        var width = MathD.Max(MinWidth, gui.CurrentNode.LayoutData.Rect.width);
        using (gui.Node(label, index).Width(width).Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
        {
            // Draw line down the middle
            //var start = new Vector2(ActiveGUI.CurrentNode.LayoutData.Rect.x + ActiveGUI.CurrentNode.LayoutData.Rect.width / 2, ActiveGUI.CurrentNode.LayoutData.Rect.y);
            //var end = new Vector2(start.x, ActiveGUI.CurrentNode.LayoutData.Rect.y + ActiveGUI.CurrentNode.LayoutData.Rect.height);
            //ActiveGUI.DrawLine(start, end, EditorStylePrefs.Instance.Borders);

            // Label
            if (!config.HasFlag(EditorGUI.PropertyGridConfig.NoLabel))
                OnLabelGUI(gui, label);

            // Value
            using (gui.Node("#_Value").Height(ItemSize).Enter())
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
            gui.Draw2D.DrawText(pretty, pos, EditorStylePrefs.Instance.LesserText * 1.5f);
        }
    }

    public virtual bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? targetValue)
    {
        var col = EditorStylePrefs.Instance.Warning * (gui.IsNodeHovered() ? 1f : 0.8f);
        var pos = gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(0, 8);
        gui.Draw2D.DrawText(targetValue.ToString(), pos, col);
        return false;
    }
}
