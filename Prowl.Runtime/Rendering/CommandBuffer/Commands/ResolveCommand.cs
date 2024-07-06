using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct ResolveCommand : RenderingCommand
    {
        public bool RTResolve;

        public Texture Source;
        public Texture Destination;

        public RenderTexture RTSource;
        public RenderTexture RTDestination;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            if (RTResolve)
            {   
                if (!RTSource.FormatEquals(RTDestination, false))
                    throw new InvalidOperationException("Destination format does not match source format for texture resolve.");

                for (int i = 0; i < RTSource.ColorBuffers.Length; i++)
                    list.ResolveTexture(RTSource.ColorBuffers[i].InternalTexture, RTDestination.ColorBuffers[i].InternalTexture);

                return;
            }

            if (!Source.Equals(Destination, false))
                throw new InvalidOperationException("Destination format does not match source format for texture resolve.");

            list.ResolveTexture(Source.InternalTexture, Destination.InternalTexture);
        }
    }
}