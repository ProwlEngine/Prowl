using Prowl.Editor.Assets;
using Prowl.Editor.Utilities;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using System.Reflection;

namespace Prowl.Editor
{
    public static class MainMenuItems
    {
        #region Assets
        public static DirectoryInfo? Directory { get; set; }
        public static bool fromAssetBrowser = false;

        [MenuItem("Create/Folder")]
        public static void CreateDir()
        {
            Directory ??= new DirectoryInfo(Project.ProjectAssetDirectory);

            DirectoryInfo dir = new(Path.Combine(Directory.FullName, "New Folder"));
            AssetDatabase.GenerateUniqueAssetPath(ref dir);
            dir.Create();
            if (fromAssetBrowser)
                AssetsBrowserWindow.StartRename(dir.FullName);
            else
                AssetsTreeWindow.StartRename(dir.FullName);
        }

        [MenuItem("Create/Material")]
        public static void CreateMaterial()
        {
            Directory ??= new DirectoryInfo(Project.ProjectAssetDirectory);

            FileInfo file = new FileInfo(Path.Combine(Directory.FullName, $"New Material.mat"));
            AssetDatabase.GenerateUniqueAssetPath(ref file);

            Material mat = new Material(Shader.Find("Defaults/Standard.shader"));
            StringTagConverter.WriteToFile(Serializer.Serialize(mat), file);
            if (fromAssetBrowser)
                AssetsBrowserWindow.StartRename(file.FullName);
            else
                AssetsTreeWindow.StartRename(file.FullName);

            AssetDatabase.Update();
            AssetDatabase.Ping(file);
        }

        [MenuItem("Create/GuiStyle")]
        public static void CreateGuiStyle()
        {
            Directory ??= new DirectoryInfo(Project.ProjectAssetDirectory);

            FileInfo file = new FileInfo(Path.Combine(Directory.FullName, $"New GuiStyle.guistyle"));
            AssetDatabase.GenerateUniqueAssetPath(ref file);

            GuiStyle style = new GuiStyle();
            StringTagConverter.WriteToFile(Serializer.Serialize(style), file);
            if (fromAssetBrowser)
                AssetsBrowserWindow.StartRename(file.FullName);
            else
                AssetsTreeWindow.StartRename(file.FullName);

            AssetDatabase.Update();
            AssetDatabase.Ping(file);
        }

        [MenuItem("Create/Script")]
        public static void CreateScript()
        {
            Directory ??= new DirectoryInfo(Project.ProjectAssetDirectory);

            FileInfo file = new FileInfo(Path.Combine(Directory.FullName, $"New Script.cs"));
            AssetDatabase.GenerateUniqueAssetPath(ref file);

            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt");
            using StreamReader reader = new StreamReader(stream);
            string script = reader.ReadToEnd();
            script = script.Replace("%SCRIPTNAME%", EditorUtils.FilterAlpha(Path.GetFileNameWithoutExtension(file.Name)));
            File.WriteAllText(file.FullName, script);
            if (fromAssetBrowser)
                AssetsBrowserWindow.StartRename(file.FullName);
            else
                AssetsTreeWindow.StartRename(file.FullName);

            AssetDatabase.Update();
            AssetDatabase.Ping(file);
        }

        #endregion

        #region Scenes

        [MenuItem("Scene/New")]
        public static void NewScene()
        {
            SceneManager.Clear();
            SceneManager.InstantiateNewScene();
        }

        [MenuItem("Scene/Save")]
        public static void SaveScene()
        {
            Scene scene = SceneManager.MainScene;
            if (scene.AssetID == Guid.Empty || !AssetDatabase.Contains(scene.AssetID))
            {
                SaveSceneAs();
                return;
            }

            if (AssetDatabase.TryGetFile(scene.AssetID, out var file))
            {
                AssetDatabase.Delete(file);

                var allGameObjects = SceneManager.AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
                scene = Scene.Create(allGameObjects);
                StringTagConverter.WriteToFile(Serializer.Serialize(scene), file);
                AssetDatabase.Update();
                AssetDatabase.Ping(file);
            }
        }

        [MenuItem("Scene/Save As")]
        public static void SaveSceneAs()
        {
            FileDialogContext imFileDialogInfo = new FileDialogContext()
            {
                title = "Save As",
                fileName = "New Scene.scene",
                directoryPath = new DirectoryInfo(Project.ProjectAssetDirectory),
                type = FileDialogType.SaveFile,
                OnComplete = (path) =>
                {
                    // Make sure path is relative to ProjectAssetDirectory
                    var file = new FileInfo(path);
                    if (!AssetDatabase.FileIsInProject(file))
                        return;

                    if (File.Exists(path))
                        AssetDatabase.Delete(file);

                    // If no extension (or wrong extension) add .scene
                    if (!file.Extension.Equals(".scene", StringComparison.OrdinalIgnoreCase))
                        file = new FileInfo(file.FullName + ".scene");

                    var allGameObjects = SceneManager.AllGameObjects.Where(x => !x.hideFlags.HasFlag(HideFlags.DontSave) && !x.hideFlags.HasFlag(HideFlags.HideAndDontSave)).ToArray();
                    Scene scene = Scene.Create(allGameObjects);
                    var tag = Serializer.Serialize(scene);
                    StringTagConverter.WriteToFile(tag, file);
                    AssetDatabase.Update();
                    AssetDatabase.Ping(file);
                }
            };
            FileDialog.Open(imFileDialogInfo);
        }

        #endregion

        #region Templates

        static Vector3 GetPosition()
        {
            // Last Focused Editor camera
            var cam = SceneViewWindow.LastFocusedCamera;
            // get position 10 units infront
            var t = cam.GameObject;
            return t.Transform.position + t.Transform.forward * 10;
        }

        [MenuItem("Template/3D Object/Cube")]
        public static void Template3DCube() => CreateDefaultModel("Cube", typeof(BoxCollider));

        [MenuItem("Template/3D Object/Sphere")]
        public static void Template3DSphere() => CreateDefaultModel("Sphere", typeof(SphereCollider));

        [MenuItem("Template/3D Object/Cylinder")]
        public static void Template3DCylinder() => CreateDefaultModel("Cylinder", typeof(CylinderCollider));

        [MenuItem("Template/3D Object/Capsule")]
        public static void Template3DCapsule() => CreateDefaultModel("Capsule", typeof(CapsuleCollider));

        [MenuItem("Template/3D Object/Plane")]
        public static void Template3DPlane() => CreateDefaultModel("Plane", typeof(MeshCollider));

        [MenuItem("Template/3D Object/Quad")]
        public static void Template3DQuad() => CreateDefaultModel("Quad", null);

        private static void CreateDefaultModel(string name, Type? component)
        {
            var original = Application.AssetProvider.LoadAsset<GameObject>($"Defaults/{name}.obj");
            if (original.IsAvailable)
            {
                var go = GameObject.Instantiate(original.Res!);
                go.Transform.position = GetPosition();
                if(component != null)
                    go.AddComponent(component);
                HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
            }
        }

        [MenuItem("Template/Lights/Ambient Light")]
        public static void TemplateAmbientLight()
        {
            var go = new GameObject("Ambient Light");
            go.AddComponent<AmbientLight>();
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }

        [MenuItem("Template/Lights/Directional Light")]
        public static void TemplateDirectionalLight()
        {
            var go = new GameObject("Directional Light");
            go.AddComponent<DirectionalLight>();
            go.Transform.position = GetPosition();
            go.Transform.localEulerAngles = new System.Numerics.Vector3(45, 70, 0);
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }

        [MenuItem("Template/Lights/Point Light")]
        public static void TemplatePointLight()
        {
            var go = new GameObject("Point Light");
            go.AddComponent<PointLight>();
            go.Transform.position = GetPosition();
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }

        [MenuItem("Template/Lights/Spot Light")]
        public static void TemplateSpotLight()
        {
            var go = new GameObject("Spot Light");
            go.AddComponent<SpotLight>();
            go.Transform.position = GetPosition();
            HierarchyWindow.SelectHandler.SetSelection(new WeakReference(go));
        }

        #endregion

    }
}
