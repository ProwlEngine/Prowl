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
        public AssetRef<Texture> TextureValue;

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

    internal struct SetBufferPropertyCommand : RenderingCommand
    {
        public string Name;
        public GraphicsBuffer BufferValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.propertyState.SetBuffer(Name, BufferValue);
        }
    }

    internal struct SetPropertyStateCommand : RenderingCommand
    {
        public PropertyState StateValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.propertyState.ApplyOverride(StateValue);
        }
    }
}