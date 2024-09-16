using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

internal class Program
{

    public static DirectoryInfo Data => new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData"));

    public static FileInfo AssemblyDLL => new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "net8.0", "CSharp.dll"));

    public static int Main(string[] args)
    {

        Application.IsPlaying = true;
        Application.DataPath = Data.FullName;


        Application.Initialize += () =>
        {

            Physics.Initialize();

            AssemblyManager.LoadExternalAssembly(AssemblyDLL.FullName, true);
            OnAssemblyLoadAttribute.Invoke();

            FileInfo StartingScene = new FileInfo(Path.Combine(Data.FullName, "scene_0.prowl"));
            Debug.Log($"Starting Scene: {StartingScene.FullName}");
            if (File.Exists(StartingScene.FullName))
            {
                var tag = BinaryTagConverter.ReadFromFile(StartingScene);
                Scene scene = Serializer.Deserialize<Scene>(tag);
                SceneManager.LoadScene(scene);
            }
        };

        Application.Update += SceneManager.Update;

        Application.Render += () =>
        {
            SceneManager.Draw();

            Graphics.EndFrame();
        };

        Application.Quitting += () =>
        {
        };

        Application.Run("Prowl Editor", 1920, 1080, new StandaloneAssetProvider(), false);

        return 0;
    }

}
