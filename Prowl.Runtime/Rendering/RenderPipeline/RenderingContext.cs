using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    public class RenderingContext
    {
        public readonly Framebuffer TargetFramebuffer;
        public Camera Camera => currentCamera;
        public List<Renderable> Renderables;


        private List<RenderingCommand> internalCommandList = new();
        private Camera currentCamera;
        public Matrix4x4 Mat_V;
        public Matrix4x4 Mat_P;

        public RenderingContext(List<Renderable> renderables, Framebuffer target)
        {
            Renderables = renderables;
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

        public Framebuffer SetupTargetCamera(Camera cam, out uint width, out uint height)
        {
            currentCamera = cam;

            Framebuffer target = TargetFramebuffer;
            width = TargetFramebuffer.Width;
            height = TargetFramebuffer.Height;

            if (cam.Target.IsAvailable)
            {
                width = cam.Target.Res!.Width;
                height = cam.Target.Res!.Height;
                target = cam.Target.Res!.Framebuffer;
            }

            Mat_V = cam.View;
            Mat_P = cam.GetProjectionMatrix(width, height);

            return target;
        }

        public List<Renderable> Cull(BoundingFrustum camFrustrum)
        {
            List<Renderable> result = new();
            foreach (var renderable in Renderables)
                //if (camFrustrum.Intersects(renderable.WorldBounds))
                    result.Add(renderable);
            return result;
        }

        public void DrawRenderers(List<Renderable> cullingResults, DrawSettings settings, LayerMask layerMask)
        {
            SortedList<double, List<Renderable>> sorted;

            if (settings.SortingMode == SortMode.FrontToBack)
                sorted = new SortedList<double, List<Renderable>>();
            else
                sorted = new SortedList<double, List<Renderable>>(new BackToFrontComparer());

            var camPos = currentCamera.Transform.position;
            foreach (var renderable in cullingResults)
            {
                if (!layerMask.HasLayer(renderable.Layer))
                    continue;

                double distance = Vector3.Distance(camPos, renderable.WorldBounds.center);

                if (!sorted.ContainsKey(distance))
                    sorted[distance] = new List<Renderable>();

                sorted[distance].Add(renderable);
            }

            // Apply Built-in Uniforms
            CommandBuffer cmd = new();
            cmd.SetMatrix("Mat_V", Mat_V);
            cmd.SetMatrix("Mat_P", Mat_P);
            cmd.SetFloat("Time", (float)Time.time);
            ExecuteCommandBuffer(cmd);
            cmd.Clear();

            //var VP = defaultUniforms.Mat_V * defaultUniforms.Mat_P;
            foreach (var pair in sorted)
                foreach (var renderable in pair.Value)
                {
                    cmd.SetMatrix("Mat_ObjectToWorld", renderable.Matrix);
                    Matrix4x4.Invert(renderable.Matrix, out Matrix4x4 inv);
                    cmd.SetMatrix("Mat_WorldToObject", inv);

                    //cmd.SetMatrix("Mat_MVP", renderable.Matrix * VP);
                    cmd.SetMatrix("Mat_MVP", renderable.Matrix * Mat_V * Mat_P);
                    ExecuteCommandBuffer(cmd);

                    renderable.Draw(this, settings);
                }

        }

        class BackToFrontComparer : IComparer<double>
        {
            public int Compare(double x, double y) => y.CompareTo(x);
        }
    }
}