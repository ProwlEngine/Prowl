using SampleBase;

namespace ComputeParticles
{
    class Program
    {
        public static void Main(string[] args)
        {
            NeoVeldridStartupWindow window = new NeoVeldridStartupWindow("Compute Particles");
            ComputeParticles computeParticles = new ComputeParticles(window);
            window.Run();
        }
    }
}
