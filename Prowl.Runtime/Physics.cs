using Jitter2;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public class PhysicalSpace
    {
        public readonly World world;

        private static double timer = 0;

        public PhysicalSpace(int iterations = 1, int substeps = 8)
        {
            world = new();
            world.SolverIterations = iterations;
            world.NumberSubsteps = substeps;
            world.ThreadModel = World.ThreadModelType.Regular;

            Physics.PhysicalSpaces.Add(this);
        }

        internal void Update(double delta)
        {
            timer += delta;
            while (timer >= Time.fixedDeltaTime) {
                world.Step((float)Time.fixedDeltaTime);
                timer -= Time.fixedDeltaTime;
            }
        }

        public void Dispose(bool clear = true)
        {
            // TODO: Causes a crash, seems to be a bug in Jitter2 where Shapes cannot be removed because they still have rigidbodies attatched to them
            //world.Clear();
            if (clear)
                Physics.PhysicalSpaces.Remove(this);
        }
    }

    public static class Physics
    {
        public static PhysicalSpace DefaultSpace => PhysicalSpaces[0];

        public readonly static List<PhysicalSpace> PhysicalSpaces = new();

        public static void Initialize()
        {
            PhysicalSpaces.Add(new PhysicalSpace()); // Default Space
        }

        public static void Update()
        {
            for(int i = 0; i < PhysicalSpaces.Count; i++) {
                PhysicalSpaces[i].Update(Time.deltaTime);
            }
        }

        public static void Dispose()
        {
            for (int i = 0; i < PhysicalSpaces.Count; i++) {
                PhysicalSpaces[i].Dispose(false);
            }
            PhysicalSpaces.Clear();
        }
    }
}
