using HexaEngine.ImGuiNET;
using System;
using System.Reflection;

namespace Prowl.Editor
{
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

        public static void ClearMenus()
        {
            Menus.Clear();
        }

        // Returns the Method and Name for this item
        public static void FindAllMenus()
        {
            List<MenuPath> values = new();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                Type[] types = null;
                try
                {
                    types = assembly.GetTypes();
                    foreach (var type in types)
                        if (type != null)
                            foreach (MethodInfo method in type.GetMethods())
                            {
                                var attribute = method.GetCustomAttribute<MenuItem>();
                                if (attribute != null)
                                    values.Add(new MenuPath(attribute.Path, () => method.Invoke(null, null)));
                            }
                }
                catch { }
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
                        child = new MenuPath(path[i], i == path.Length-1 ? value.Method : null);
                        node.Children.Add(child);
                    }
                    node = child;
                }
            }

            // Done
            Menus = trees;
        }

        public static void DrawMenuRoot(string root)
        {
            if (Menus == null) return;
            if (root == null) return;
            if (!Menus.ContainsKey(root)) return;
            var node = Menus[root];
            if (node.Children.Count == 0) return;
            //if (ImGui.BeginPopup("##Menu_"+ root))
            if (ImGui.BeginMenu(node.Path))
            {
                foreach (var child in node.Children)
                    DrawMenu(child);
                ImGui.EndMenu();
            }
        }

        public static void DrawMenuPopupRoot(string root)
        {
            if (Menus == null) return;
            if (root == null) return;
            if (!Menus.ContainsKey(root)) return;
            var node = Menus[root];
            if (node.Children.Count == 0) return;
            if (ImGui.BeginPopup("##Menu_"+ root))
            {
                foreach (var child in node.Children)
                    DrawMenu(child);
                ImGui.EndMenu();
            }
        }

        static void DrawMenu(MenuPath menu)
        {
            if (menu == null) return;
            if (menu.Children.Count == 0)
            {
                if (ImGui.MenuItem(menu.Path))
                    menu.Method?.Invoke();
            }
            else
            {
                if (ImGui.BeginMenu(menu.Path))
                {
                    foreach (var child in menu.Children)
                        DrawMenu(child);
                    ImGui.EndMenu();
                }
            }
        }

    }
}
