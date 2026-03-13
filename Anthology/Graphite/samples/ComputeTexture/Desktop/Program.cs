using SampleBase;

namespace ComputeTexture
{
    class Program
    {
        public static void Main(string[] args)
        {
            VeldridStartupWindow window = new VeldridStartupWindow("Compute Texture");
            ComputeTexture computeTexture = new ComputeTexture(window);
            window.Run();
        }
    }
}
