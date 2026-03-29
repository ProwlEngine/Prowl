using SampleBase;

namespace Offscreen
{
    class Program
    {
        public static void Main(string[] args)
        {
            NeoVeldridStartupWindow window = new NeoVeldridStartupWindow("Offscreen");
            OffscreenApplication offscreen = new OffscreenApplication(window);
            window.Run();
        }
    }
}
