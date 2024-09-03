// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
