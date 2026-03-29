using SampleBase;

namespace TexturedCube
{
    class Program
    {
        public static void Main(string[] args)
        {
            NeoVeldridStartupWindow window = new NeoVeldridStartupWindow("Textured Cube");
            TexturedCube texturedCube = new TexturedCube(window);
            window.Run();
        }
    }
}
