// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime
{
    public readonly struct StageInput : IEquatable<StageInput>
    {
        public readonly string semantic;
        public readonly VertexElementFormat format;

        public StageInput(string semantic, VertexElementFormat format)
        {
            this.semantic = semantic;
            this.format = format;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not StageInput other)
                return false;

            return Equals(other);
        }

        public bool Equals(StageInput other)
            => semantic == other.semantic;

        public override int GetHashCode()
            => semantic.GetHashCode();
    }
}
