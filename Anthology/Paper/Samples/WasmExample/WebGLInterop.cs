using System.Runtime.InteropServices.JavaScript;

namespace WasmExample;

/// <summary>
/// JS interop bindings for WebGL2 rendering.
/// </summary>
public static partial class WebGLInterop
{
    [JSImport("webgl.initWebGL", "main.js")]
    internal static partial void InitWebGL(string canvasId);

    [JSImport("webgl.getCanvasWidth", "main.js")]
    internal static partial int GetCanvasWidth();

    [JSImport("webgl.getCanvasHeight", "main.js")]
    internal static partial int GetCanvasHeight();

    [JSImport("webgl.createTexture", "main.js")]
    internal static partial void CreateTexture(int texId, int width, int height);

    [JSImport("webgl.setTextureData", "main.js")]
    internal static partial void SetTextureData(int texId, int x, int y, int w, int h, byte[] data);

    [JSImport("webgl.createFloatTexture", "main.js")]
    internal static partial void CreateFloatTexture(int texId, int width, int height, int components, double[] data);

    [JSImport("webgl.render", "main.js")]
    internal static partial void Render(
        byte[] vertexBytes,
        int[] indexData,
        int[] drawCallInfo,
        double[] scissorData,
        double[] brushData,
        double canvasScale);
}
