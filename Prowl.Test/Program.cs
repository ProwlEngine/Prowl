using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

namespace Prowl.Standalone;

internal static class Program
{

    public static DirectoryInfo Data => new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
    static Texture2D catTex;

    private static Mesh quadMesh = Mesh.CreateSphere(1f, 40, 40);

    public static int Main(string[] args)
    {

        Application.isPlaying = true;
        Application.DataPath = Data.FullName;

        Application.Initialize += () =>
        {
            catTex = Texture2DLoader.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cat.png"));
        };

        Application.Update += () =>
        {

        };

        Application.Render += () =>
        {
            Graphics.StartFrame(catTex);

            Graphics.DrawNDCQuad(quadMesh);

            Graphics.EndFrame();
        };

        Application.Quitting += () =>
        {

        };

        Application.Run("Prowl Editor", 1920, 1080, null, false);

        return 0;
    }

}
