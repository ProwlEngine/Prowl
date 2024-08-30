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
            state.pipelineResources.SetVector(Name, Value);
        }
    }

    internal struct SetPropertyArrayCommand : RenderingCommand
    {
        public string Name;
        public Vector4[] Value;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.pipelineResources.SetVectorArray(Name, Value);
        }
    }

    internal struct SetTexturePropertyCommand : RenderingCommand
    {
        public string Name;
        public AssetRef<Texture> TextureValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.pipelineResources.SetTexture(Name, TextureValue.Res);
        }
    }

    internal struct SetMatrixPropertyCommand : RenderingCommand
    {
        public string Name;
        public Matrix4x4 MatrixValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.pipelineResources.SetMatrix(Name, MatrixValue);
        }
    }

    internal struct SetMatrixArrayPropertyCommand : RenderingCommand
    {
        public string Name;
        public Matrix4x4[] MatrixValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.pipelineResources.SetMatrixArray(Name, MatrixValue);
        }
    }

    internal struct SetBufferPropertyCommand : RenderingCommand
    {
        public string Name;
        public GraphicsBuffer BufferValue;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            state.pipelineResources.SetBuffer(Name, BufferValue);
        }
    }
}
