using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.Components;
using Prowl.Runtime.Resources;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Serializer;
using Prowl.Runtime.Serializer;
using Prowl.Runtime.Utils;
using System.IO;
using System.Reflection;

namespace Prowl.Editor.EditorWindows
{
    public static class MainMenuItems
    {
        #region Assets
        public static DirectoryInfo? Directory { get; set; }

        [MenuItem("Create/Material")]
        public static void CreateMaterial()
        {
            Directory ??= new DirectoryInfo(Project.ProjectAssetDirectory);
            Material mat = new Material(Shader.Find("Defaults/Standard.shader"));
            FileInfo file = new FileInfo(Directory + "/NewMaterial.mat");
            while (file.Exists)
            {
                file = new FileInfo(file.FullName.Replace(".mat", "") + " new.mat");
            }
            StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(mat), file);

            Selection.Select(file);
            AssetDatabase.Ping(AssetDatabase.FileToRelative(file));
        }

        [MenuItem("Create/Script")]
        public static void CreateScript()
        {
            Directory ??= new DirectoryInfo(Project.ProjectAssetDirectory);
            FileInfo file = new FileInfo(Directory + "/New Script.cs");
            while (file.Exists)
            {
                file = new FileInfo(file.FullName.Replace(".cs", "") + " new.cs");
            }
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt");
            using StreamReader reader = new StreamReader(stream);
            File.WriteAllText(file.FullName, reader.ReadToEnd());

            Selection.Select(file);
            AssetDatabase.Ping(AssetDatabase.FileToRelative(file));
        }

        #endregion

        #region Scenes

        [MenuItem("Scene/New")]
        public static void NewScene()
        {
            Selection.Clear();
            SceneManager.Clear();
            SceneManager.InstantiateNewScene();
        }

        [MenuItem("Scene/Save")]
        public static void SaveScene()
        {
            Scene scene = SceneManager.MainScene;
            if(scene.AssetID == Guid.Empty || !AssetDatabase.Contains(scene.AssetID))
            {
                SaveSceneAs();
                return;
            }

            var relativeAssetPath = AssetDatabase.GUIDToAssetPath(scene.AssetID);
            AssetDatabase.Remove(relativeAssetPath);
            var file = AssetDatabase.RelativeToFile(relativeAssetPath);

            var allGameObjects = SceneManager.AllGameObjects.ToArray();
            scene.GameObjects = (ListTag)TagSerializer.Serialize(allGameObjects);
            StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(scene), file);
            Selection.Select(file);
            AssetDatabase.Ping(AssetDatabase.FileToRelative(file));
        }

        [MenuItem("Scene/Save As")]
        public static void SaveSceneAs()
        {
            ImFileDialogInfo imFileDialogInfo = new ImFileDialogInfo()
            {
                title = "Save Scene As",
                fileName = "New Scene.scene",
                directoryPath = new DirectoryInfo(Project.ProjectAssetDirectory),
                type = ImGuiFileDialogType.SaveFile,
                OnComplete = (path) =>
                {
                    // Make sure path is relative to ProjectAssetDirectory
                    var file = new FileInfo(path);
                    if (!AssetDatabase.FileIsInProject(file))
                        return;

                    if (File.Exists(path))
                        AssetDatabase.Remove(AssetDatabase.FileToRelative(file));

                    // If no extension (or wrong extension) add .scene
                    if (!file.Extension.Equals(".scene", StringComparison.OrdinalIgnoreCase))
                        file = new FileInfo(file.FullName + ".scene");

                    var allGameObjects = SceneManager.AllGameObjects.ToArray();
                    Scene scene = new Scene();
                    scene.GameObjects = (ListTag)TagSerializer.Serialize(allGameObjects);
                    var tag = (CompoundTag)TagSerializer.Serialize(scene);
                    StringTagConverter.WriteToFile(tag, file);
                    Selection.Select(file);
                    AssetDatabase.Ping(AssetDatabase.FileToRelative(file));
                }   
            };
            ImGuiFileDialog.FileDialog(imFileDialogInfo);
        }

        #endregion

        #region Templates

        static Vector3 GetPosition()
        {
            // Last Focused Editor camera
            var cam = ViewportWindow.LastFocusedCamera;
            // get position 10 units infront
            return cam.GameObject.GlobalPosition + cam.GameObject.Forward * 10;
        }

        [MenuItem("Template/Lights/Ambient Light")]
        public static void TemplateAmbientLight()
        {
            var go = new GameObject("Ambient Light");
            go.GlobalPosition = GetPosition();
            go.AddComponent<AmbientLight>();
            Selection.Select(go);
        }

        [MenuItem("Template/Lights/Directional Light")]
        public static void TemplateDirectionalLight()
        {
            var go = new GameObject("Directional Light");
            go.GlobalPosition = GetPosition();
            go.AddComponent<DirectionalLight>();
            go.Rotation = new System.Numerics.Vector3(45, 70, 0);
            Selection.Select(go);
        }

        [MenuItem("Template/Lights/Point Light")]
        public static void TemplatePointLight()
        {
            var go = new GameObject("Point Light");
            go.GlobalPosition = GetPosition();
            go.AddComponent<PointLight>();
            Selection.Select(go);
        }

        [MenuItem("Template/Lights/Spot Light")]
        public static void TemplateSpotLight()
        {
            var go = new GameObject("Spot Light");
            go.GlobalPosition = GetPosition();
            go.AddComponent<SpotLight>();
            Selection.Select(go);
        }

        #endregion

    }
}
