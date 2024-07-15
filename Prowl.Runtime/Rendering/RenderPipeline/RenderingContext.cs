using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Veldrid;
using static Prowl.Runtime.Camera;

namespace Prowl.Runtime.RenderPipelines
{
    public class RenderingContext
    {
        public readonly RenderTexture TargetTexture;
        public CameraData Camera => currentCamera;
        public List<Renderable> Renderables;
        public string PipelineName;


        private List<RenderingCommand> internalCommandList = new();
        private CameraData currentCamera;
        public Matrix4x4 Mat_V;
        public Matrix4x4 Mat_P;

        public RenderingContext(string pipelineName, List<Renderable> renderables, RenderTexture target)
        {
            Renderables = renderables;
            TargetTexture = target;
            PipelineName = pipelineName;
        }

        public void ExecuteCommandBuffer(CommandBuffer buffer, bool clear = true)
        {
            internalCommandList.AddRange(buffer.Buffer);
            if(clear)
                buffer.Clear();
        }       

        private void InitializeRenderState(out CommandList commandList, out RenderState state)
        {
            commandList = null;
            state = null;

            if (internalCommandList.Count == 0)
                return;

            commandList = Graphics.GetCommandList();
            state = new RenderState();

            state.SetFramebuffer(TargetTexture.Framebuffer);
            commandList.SetFramebuffer(TargetTexture.Framebuffer);

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

        public void SetupTargetCamera(CameraData cam, uint width, uint height)
        {
            currentCamera = cam;

            Mat_V = cam.View;
            Mat_P = cam.GetProjectionMatrix(width, height);
        }

        public List<Renderable> Cull(BoundingFrustum camFrustrum)
        {
            List<Renderable> result = new();
            foreach (var renderable in Renderables)
                if (renderable.WorldBounds != null)// && camFrustrum.Intersects(renderable.WorldBounds))
                    result.Add(renderable);
                else
                    result.Add(renderable);
            return result;
        }

        public void DrawRenderers(CommandBuffer buffer, List<Renderable> sorted, DrawSettings settings, LayerMask layerMask)
        {
            if (sorted == null || sorted.Count == 0) return;

            // Apply Built-in Uniforms
            buffer.SetMatrix("Mat_V", Mat_V);
            buffer.SetMatrix("Mat_P", Mat_P);
            buffer.SetFloat("Time", (float)Time.time);

            //var VP = defaultUniforms.Mat_V * defaultUniforms.Mat_P;
            foreach (var renderable in sorted)
            {
                if (!layerMask.HasLayer(renderable.Layer))
                    continue;

                if (renderable.Material == null || !renderable.Material.Shader.IsAvailable)
                    continue;

                // Check for valid material passes
                var passes = renderable.Material.Shader.Res.GetPassesWithTag("RenderOrder", settings.RenderOrder);

                if (passes.Count > 0)
                {
                    buffer.SetMatrix("Mat_ObjectToWorld", renderable.Matrix);
                    Matrix4x4.Invert(renderable.Matrix, out Matrix4x4 inv);
                    buffer.SetMatrix("Mat_WorldToObject", inv);

                    //cmd.SetMatrix("Mat_MVP", renderable.Matrix * VP);
                    buffer.SetMatrix("Mat_MVP", renderable.Matrix * Mat_V * Mat_P);

                    // Push Properties
                    buffer.ApplyPropertyState(renderable.Properties);

                    // Draw each pass
                    foreach (var pass in passes)
                        renderable.Draw(buffer, pass, this);
                }
            }

        }

        public SortedList<double, List<Renderable>> SortRenderables(List<Renderable> cullingResults, SortMode sortingMode)
        {
            if(cullingResults == null || cullingResults.Count == 0)
                return new SortedList<double, List<Renderable>>();

            SortedList<double, List<Renderable>> sorted;

            if (sortingMode == SortMode.FrontToBack)
                sorted = new SortedList<double, List<Renderable>>();
            else
                sorted = new SortedList<double, List<Renderable>>(new BackToFrontComparer());

            var camPos = currentCamera.Position;
            foreach (var renderable in cullingResults)
            {
                double distance = 0;
                if (renderable.WorldBounds.HasValue)
                    distance = Vector3.Distance(camPos, renderable.WorldBounds.Value.center);

                if (!sorted.ContainsKey(distance))
                    sorted[distance] = new List<Renderable>();

                sorted[distance].Add(renderable);
            }

            return sorted;
        }

        class BackToFrontComparer : IComparer<double>
        {
            public int Compare(double x, double y) => y.CompareTo(x);
        }
    }
}