using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    internal struct SetKeywordCommand : RenderingCommand
    {
        public string Name;
        public string Value;

        readonly void RenderingCommand.ExecuteCommand(CommandList list, ref RenderState state)
        {
            state.keywordState.SetKey(Name, Value);
        }
    }
}