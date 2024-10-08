using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

namespace Prowl.Desktop;

public static class DesktopPlayer
{
    public static DirectoryInfo Data => new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData"));

    public static int Main()
    {

        Application.IsPlaying = true;
        Application.DataPath = Data.FullName;


        Application.Initialize += () =>
        {
            Physics.Initialize();

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
