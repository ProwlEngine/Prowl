using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetPropertyCommand : RenderingCommand
    {
        public string Name;
        public Vector4 Value;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.propertyState.SetVector(Name, Value);
        }
    }

    internal struct SetTexturePropertyCommand : RenderingCommand
    {
        public string Name;
        public Texture TextureValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.propertyState.SetTexture(Name, TextureValue);
        }
    }

    internal struct SetMatrixPropertyCommand : RenderingCommand
    {
        public string Name;
        public Matrix4x4 MatrixValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.propertyState.SetMatrix(Name, MatrixValue);
        }
    }
}