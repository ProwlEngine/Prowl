using Prowl.Runtime.Utils;
using Veldrid;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace Prowl.Runtime.RenderPipelines
{
    public class RenderingContext
    {
        public readonly Framebuffer TargetFramebuffer;

        private List<RenderingCommand> internalCommandList = new();

        public RenderingContext(Framebuffer target)
        {
            TargetFramebuffer = target;
        }

        public void ExecuteCommandBuffer(CommandBuffer buffer)
        {
            internalCommandList.AddRange(buffer.Buffer);
        }       

        private void InitializeRenderState(out CommandList commandList, out RenderState state)
        {
            commandList = null;
            state = null;

            if (internalCommandList.Count == 0)
                return;

            commandList = Graphics.GetCommandList();
            state = new RenderState();

            state.SetFramebuffer(TargetFramebuffer);
            commandList.SetFramebuffer(TargetFramebuffer);

            for (int i = 0; i < internalCommandList.Count; i++)
            {
                try 
                {
                    internalCommandList[i].ExecuteCommand(commandList, state);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to execute command: {internalCommandList[i]}", ex);
                }
            }

            internalCommandList.Clear();
        } 

        public void Submit()
        {
            InitializeRenderState(out CommandList commandListToExecute, out RenderState renderStateToExecute);

            Graphics.ExecuteCommandList(commandListToExecute);
            
            commandListToExecute.Dispose();
            renderStateToExecute.Dispose();
        }

        public async Task SubmitAsync()
        {
            InitializeRenderState(out CommandList commandListToExecute, out RenderState renderStateToExecute);
            
            await Graphics.AsyncExecuteCommandList(commandListToExecute);
            
            commandListToExecute.Dispose();
            renderStateToExecute.Dispose();
        }
    }
}