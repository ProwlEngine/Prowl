using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct DebugGroupCommand : RenderingCommand
    {
        public string Name;
        public bool Pop;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, RenderState state)
        {
            if (Pop)
                list.PopDebugGroup();
            else
                list.PushDebugGroup(Name);
        }
    }
}