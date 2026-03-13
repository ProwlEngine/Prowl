using SampleBase;

namespace AnimatedMesh
{
    class Program
    {
        public static void Main(string[] args)
        {
            VeldridStartupWindow window = new VeldridStartupWindow("Animated Mesh");
            AnimatedMesh animatedMesh = new AnimatedMesh(window);
            window.Run();
        }
    }
}
