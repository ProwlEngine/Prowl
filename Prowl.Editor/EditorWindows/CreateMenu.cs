using Prowl.Runtime.Assets;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;
using System.Reflection;

namespace Prowl.Editor.EditorWindows
{
    public static class CreateMenu
    {

        [MenuItem("Create/Material")]
        public static void CreateMaterial()
        {
            Material mat = new Material(Shader.Find("Defaults/Standard.shader"));
            FileInfo file = new FileInfo(AssetBrowserWindow.CurrentActiveDirectory + "/NewMaterial.mat");
            while (file.Exists)
            {
                file = new FileInfo(file.FullName.Replace(".mat", "") + " new.mat");
            }
            File.WriteAllText(file.FullName, JsonUtility.Serialize(mat));
        }

        [MenuItem("Create/Script")]
        public static void CreateScript()
        {
            FileInfo file = new FileInfo(AssetBrowserWindow.CurrentActiveDirectory + "/New Script.cs");
            while (file.Exists)
            {
                file = new FileInfo(file.FullName.Replace(".cs", "") + " new.cs");
            }
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt");
            using StreamReader reader = new StreamReader(stream);
            File.WriteAllText(file.FullName, reader.ReadToEnd());
        }

    }
}
