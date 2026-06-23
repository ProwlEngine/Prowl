namespace Prowl.Graphite.OpenGL;

internal partial class OpenGLGraphicsDevice
{
    private void ExecutorActiveFrame_CheckActive()
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (_executorActiveFrame == null)
        {
            throw new RenderException(
                "A frame-dependent command was processed by the GL execution thread with no active frame. " +
                "Record draws and dispatches between BeginFrame and EndFrame, and submit them before EndFrame.");
        }
    }
}
