using HexaEngine.ImGuiNET;
using Prowl.Editor.EditorWindows;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;
using System.Reflection;

namespace Prowl.Editor
{
    public static class CreateAssetMenuHandler
    {
        public static Dictionary<string, Type> assets = new();

        public static void ClearMenus()
        {
            assets.Clear();
        }

        // Returns the Method and Name for this item
        public static void FindAllMenus()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
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
                                assets.Add(attribute.Name, type);
                        }
                }
                catch { }
            }
        }

        public static void DrawMenuItems()
        {
            foreach (var asset in assets)
            {
                if (ImGui.MenuItem(asset.Key))
                {
                    var obj = Activator.CreateInstance(asset.Value);
                    FileInfo file = new FileInfo(AssetBrowserWindow.CurrentActiveDirectory + $"/New {asset.Key}.scriptobj");
                    while (file.Exists)
                    {
                        file = new FileInfo(file.FullName.Replace(".scriptobj", "") + " New.scriptobj");
                    }
                    File.WriteAllText(file.FullName, JsonUtility.Serialize(obj, obj.GetType()));
                }
            }
        }

    }
}
