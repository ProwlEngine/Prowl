using SampleBase;

namespace Instancing
{
    class Program
    {
        public static void Main(string[] args)
        {
            NeoVeldridStartupWindow window = new NeoVeldridStartupWindow("Instancing");
            InstancingApplication instancing = new InstancingApplication(window);
            window.Run();
        }
    }
}
