using Prowl.Runtime.Utils;


namespace Prowl.Runtime.RenderPipelines
{
    [CreateAssetMenu("RenderPipeline")]
    public sealed class DefaultRenderPipeline : RenderPipeline
    {
        public override void Render(RenderingContext context, Camera[] cameras)
        {
            // Create and schedule a command to clear the current render target
            var rootBuffer = new CommandBuffer();
            rootBuffer.SetRenderTarget(context.TargetFramebuffer);
            rootBuffer.ClearRenderTarget(true, true, Color.black);

            context.ExecuteCommandBuffer(rootBuffer);

            // Create and schedule a command to clear the current render target
            foreach (var cam in cameras)
            {
                var camBuffer = new CommandBuffer();

                // Update the value of built-in shader variables, based on the current Camera
                var target = context.SetupTargetCamera(cam, out var width, out var height);

                camBuffer.SetRenderTarget(target);

                if (cam.DoClear)
                    camBuffer.ClearRenderTarget(true, true, cam.ClearColor);

                // Get the culling parameters from the current Camera
                var camFrustrum = cam.GetFrustrum(width, height);
                
                // Use the culling parameters to perform a cull operation, and store the results
                var cullingResults = context.Cull(camFrustrum);

                DrawSettings settings = new("Opaque", SortMode.FrontToBack);

                // Schedule a command to draw the geometry, based on the settings you have defined
                context.DrawRenderers(cullingResults, settings, cam.LayerMask);

#warning TODO: Skybox

                context.ExecuteCommandBuffer(camBuffer);
            }

            // Instruct the graphics API to perform all scheduled commands
            context.Submit();
        }
    }
}