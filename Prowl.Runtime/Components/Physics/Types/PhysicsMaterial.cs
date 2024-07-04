using BepuPhysics.Constraints;

namespace Prowl.Runtime
{
    public struct PhysicsMaterial
    {
        //__Narrow__Settings__
        public SpringSettings SpringSettings;
        public float FrictionCoefficient;
        public float MaximumRecoveryVelocity;
        public bool IsTrigger;


        public static bool AllowContactGeneration(PhysicsMaterial a, PhysicsMaterial b)
        {
            return true;
        }

    }
}