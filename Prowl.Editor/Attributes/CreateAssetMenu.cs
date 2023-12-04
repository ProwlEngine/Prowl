using Prowl.Editor.EditorWindows;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Serialization;
using Prowl.Runtime.Serializer;
using Prowl.Runtime.Utils;
using System.Reflection;
using static Prowl.Editor.MenuItem;

namespace Prowl.Editor
{
    public static class CreateAssetMenuHandler
    {
        public static void FindAllMenus()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<(string, Type)> values = new();
            foreach (var assembly in assemblies)
            {
                Type[] types = null;
                try
                {
                    types = assembly.GetTypes();
                    foreach (var type in types)
                        if (type != null && type.IsAssignableTo(typeof(ScriptableObject)))
                        {
                            var attribute = type.GetCustomAttribute<CreateAssetMenu>();
                            if (attribute != null)
                                values.Add(("Create/" + attribute.Name, type));
                        }
                }
                catch { }
            }

            // values is now a list of all methods with the Menu Paths
            // We need to sort them into possibly multiple trees
            Dictionary<string, MenuPath> trees = new();
            foreach (var value in values)
            {
                var path = value.Item1.Split('/');
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
                        child = new MenuPath(path[i], i == path.Length - 1 ? () => { CreateAsset(value.Item2); } : null);
                        node.Children.Add(child);
                    }
                    node = child;
                }
            }

            // Set menus as trees for testing
            //Menus = trees;
            // Add trees into Menu without overwriting
            if (!Menus.ContainsKey("Create"))
                Menus["Create"] = trees["Create"];
            else
            {
                foreach (var child in trees["Create"].Children)
                    Menus["Create"].Children.Add(child);
            }

        }

        public static void CreateAsset(Type type)
        {
            CreateMenu.Directory ??= new DirectoryInfo(Project.ProjectAssetDirectory);
            var obj = Activator.CreateInstance(type);
            FileInfo file = new FileInfo(CreateMenu.Directory + $"/New {type.Name}.scriptobj");
            while (file.Exists)
            {
                file = new FileInfo(file.FullName.Replace(".scriptobj", "") + " New.scriptobj");
            }
            StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(obj), file);
        }
    }
}
