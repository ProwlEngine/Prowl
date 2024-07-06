using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct ManualDrawCommand : RenderingCommand
    {
        public uint InstanceCount;
        public uint IndexCount;
        public uint IndexOffset;
        public int VertexOffset;
        public uint InstanceStart;


        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {   
            list.DrawIndexed(
                indexCount: IndexCount,
                instanceCount: InstanceCount,
                indexStart: IndexOffset,
                vertexOffset: VertexOffset,
                instanceStart: InstanceStart
            );
        }
    }
}