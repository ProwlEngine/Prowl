using BepuPhysics.Constraints;

namespace Prowl.Runtime
{
    public sealed class PhysicsMaterial : EngineObject
    {
        //Narrow
        public float AngularFrequency;
        public float TwiceDampingRatio;

        public float FrictionCoefficient;
        public float MaximumRecoveryVelocity;
        public byte ColliderGroupMask;
        public bool Trigger;

        //Pose
        public bool IgnoreGravity;

        private SpringSettings NarrowSpringSettings => new SpringSettings
        {
            AngularFrequency = AngularFrequency,
            TwiceDampingRatio = TwiceDampingRatio
        };
    }
}
