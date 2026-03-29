using SampleBase;

namespace ComputeTexture
{
    class Program
    {
        public static void Main(string[] args)
        {
            NeoVeldridStartupWindow window = new NeoVeldridStartupWindow("Compute Texture");
            ComputeTexture computeTexture = new ComputeTexture(window);
            window.Run();
        }
    }
}
