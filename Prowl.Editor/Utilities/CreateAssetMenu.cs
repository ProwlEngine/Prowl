// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

using static Prowl.Editor.MenuItem;

namespace Prowl.Editor;

public static class CreateAssetMenuHandler
{
    [OnAssemblyLoad(1)]
    public static void FindAllMenus()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        List<(string, Type)> values = new();
        foreach (var assembly in assemblies)
        {
            try
            {
                Type[] types = assembly.GetTypes();
                foreach (var type in types)
                    if (type.IsAssignableTo(typeof(ScriptableObject)))
                    {
                        var attribute = type.GetCustomAttribute<CreateAssetMenu>();
                        if (attribute != null)
                            values.Add(("Assets/" + attribute.Name, type));
                    }
            }
            catch
            {
                // ignored
            }
        }

        foreach (var value in values)
        {
            var path = value.Item1.Split('/');
            // Root node
            MenuPath node = new MenuPath(path[0], null);
            if (Menus.ContainsKey(path[0]))
                node = Menus[path[0]]; // We already have this root, lets start there instead of a new root
            else
                Menus.Add(path[0], node); // This root doesnt exist yet, create it
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
    }

    public static void CreateAsset(Type type)
    {
        EditorGuiManager.Directory ??= Project.Active.AssetDirectory;
        var obj = Activator.CreateInstance(type);
        FileInfo file = new FileInfo(EditorGuiManager.Directory + $"/New {type.Name}.scriptobj");
        while (File.Exists(file.FullName))
        {
            file = new FileInfo(file.FullName.Replace(".scriptobj", "") + " New.scriptobj");
        }
        StringTagConverter.WriteToFile(Serializer.Serialize(obj), file);
        AssetDatabase.Update();
        AssetDatabase.Ping(file);
    }
}
