using Prowl.Runtime.Assets;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;

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

    }
}
