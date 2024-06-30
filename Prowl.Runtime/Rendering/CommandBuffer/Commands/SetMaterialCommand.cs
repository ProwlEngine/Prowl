using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetMaterialCommand : RenderingCommand
    {
        public Material Material;
        public int Pass;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            state.activeMaterial = Material;
            state.activePass = Pass;
            state.propertyState.ApplyOverride(Material.Properties);
        }
    }
}