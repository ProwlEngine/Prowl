using SampleBase;

namespace AnimatedMesh
{
    class Program
    {
        public static void Main(string[] args)
        {
            NeoVeldridStartupWindow window = new NeoVeldridStartupWindow("Animated Mesh");
            AnimatedMesh animatedMesh = new AnimatedMesh(window);
            window.Run();
        }
    }
}
