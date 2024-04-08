using Prowl.Runtime;
using Prowl.Runtime.Rendering.OpenGL;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Standalone;

internal class Program {

    public static DirectoryInfo Data => new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData"));

    public static FileInfo AssemblyDLL => new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "net8.0", "CSharp.dll"));

    public static int Main(string[] args) {

        Application.isPlaying = true;
        Application.isActivelyPlaying = true;
        Application.DataPath = Data.FullName;


        Application.Initialize += () => {
            AssemblyManager.LoadExternalAssembly(AssemblyDLL.FullName, true);

            FileInfo StartingScene = new FileInfo(Path.Combine(Data.FullName, "level.prowl"));
            if (File.Exists(StartingScene.FullName))
            {
                SerializedProperty tag = BinaryTagConverter.ReadFromFile(StartingScene);
                Scene scene = Serializer.Deserialize<Scene>(tag);
                SceneManager.LoadScene(scene);
            }
        };

        Application.Update += (delta) => {
            Physics.Update();
            SceneManager.Update();
        };

        Application.Render += (delta) => {
            Graphics.StartFrame();

            SceneManager.Draw();

            Graphics.EndFrame();
        };

        Application.Quitting += () => {
        };

        Application.Run("Prowl Editor", 1920, 1080, new StandaloneAssetProvider(), false);

        return 0;
    }
    
}
