// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;

namespace Prowl.Editor;
//public class TreeNode<T>
//{
//    public T Data { get; set; }
//    public List<TreeNode<T>> Children { get; set; }
//
//    public TreeNode(T data)
//    {
//        Data = data;
//        Children = new List<TreeNode<T>>();
//    }
//}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal class MenuItem : Attribute
{
    public string Path { get; }
    public MenuItem(string path)
    {
        Path = path;
    }

    public class MenuPath
    {
        public string Path { get; }
        public Action? Method { get; }
        public List<MenuPath> Children { get; set; } = new();
        public MenuPath(string path, Action? method)
        {
            Path = path;
            Method = method;
        }
    }

    public static Dictionary<string, MenuPath> Menus = new();

    [OnAssemblyUnload]
    public static void ClearMenus()
    {
        Menus.Clear();
    }

    [OnAssemblyLoad]
    public static void FindAllMenus()
    {
        List<MenuPath> values = new();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                Type[] types = assembly.GetTypes();
                foreach (var type in types)
                    foreach (MethodInfo method in type.GetMethods())
                    {
                        var attribute = method.GetCustomAttribute<MenuItem>();
                        if (attribute != null)
                            values.Add(new MenuPath(attribute.Path, () => method.Invoke(null, null)));
                    }
            }
            catch
            {
                // ignored
            }
        }

        // values is now a list of all methods with the Menu Paths
        // We need to sort them into possibly multiple trees
        Dictionary<string, MenuPath> trees = new();
        foreach (var value in values)
        {
            var path = value.Path.Split('/');
            // Root node
            MenuPath node = new MenuPath(path[0], null);
            if (trees.ContainsKey(path[0]))
                node = trees[path[0]]; // We already have this root, lets start there instead of a new root
            else
                trees.Add(path[0], node); // This root doesnt exist yet, create it
            // Add the rest of the path
            for (int i = 1; i < path.Length; i++)
            {
                var child = node.Children.FirstOrDefault(x => x.Path == path[i]);
                if (child == null)
                {
                    // Only place the Method at the leaf node
                    child = new MenuPath(path[i], i == path.Length - 1 ? value.Method : null);
                    node.Children.Add(child);
                }
                node = child;
            }
        }

        // Done
        Menus = trees;
    }

    public static MenuPath? GetMenuPath(string root)
    {
        if (!Menus.ContainsKey(root)) return null;
        return Menus[root];
    }

    public static bool DrawMenuRoot(string root, bool simpleRoot = false, Size? rootSize = null)
    {
        if (!Menus.ContainsKey(root)) return false;
        var node = Menus[root];
        if (node.Children.Count == 0) return false;

        bool changed = false;
        changed |= DrawMenu(node, simpleRoot, 0, rootSize);
        //foreach (var child in node.Children)
        //    changed |= DrawMenu(child);
        return changed;
    }

    public static bool DrawMenu(MenuPath menu, bool simpleRoot, int depth, Size? rootSize = null)
    {
        if (menu.Children.Count == 0)
        {
            if (EditorGUI.StyledButton(menu.Path))
            {
                menu.Method?.Invoke();
                return true;
            }
        }
        else
        {
            if (EditorGUI.StyledButton(menu.Path))
            {
                Vector2 pos = Gui.ActiveGUI.PreviousNode.LayoutData.Rect.TopRight;
                if (simpleRoot)
                    pos = Gui.ActiveGUI.PreviousNode.LayoutData.Rect.BottomLeft;
                Gui.ActiveGUI.OpenPopup(menu.Path + "Popup", pos);
            }

            // Enter the Button's Node
            using (Gui.ActiveGUI.PreviousNode.Enter())
            {
                if (rootSize != null)
                    Gui.ActiveGUI.CurrentNode.Width(rootSize.Value);

                // Draw a > to indicate a popup
                if (depth != 0 || !simpleRoot)
                {
                    Rect rect = Gui.ActiveGUI.CurrentNode.LayoutData.Rect;
                    rect.x = rect.x + rect.width - 25;
                    rect.width = 20;
                    Gui.ActiveGUI.Draw2D.DrawText(FontAwesome6.ChevronRight, rect, Color.white);
                }
            }

            if (Gui.ActiveGUI.BeginPopup(menu.Path + "Popup", out var node))
            {
                ArgumentNullException.ThrowIfNull(node);

                using (node.Width(150).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
                {
                    bool changed = false;
                    foreach (var child in menu.Children)
                        changed |= DrawMenu(child, false, depth + 1);
                    return changed;
                }
            }
        }

        return false;
    }

}
