using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;

namespace Photonic.Demo;

internal static class Program
{
    [System.STAThread] // required by System.Windows.Forms.OpenFileDialog
    private static void Main()
    {
        var gws = GameWindowSettings.Default;
        var nws = new NativeWindowSettings
        {
            ClientSize = new Vector2i(1600, 900),
            Title = "Prowl.Photonic Demo",
            Profile = ContextProfile.Core,
            APIVersion = new System.Version(4, 3),
        };
        using var app = new DemoWindow(gws, nws);
        app.Run();
    }
}
