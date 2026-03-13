using SampleBase;

namespace ComputeParticles
{
    class Program
    {
        public static void Main(string[] args)
        {
            VeldridStartupWindow window = new VeldridStartupWindow("Compute Particles");
            ComputeParticles computeParticles = new ComputeParticles(window);
            window.Run();
        }
    }
}
