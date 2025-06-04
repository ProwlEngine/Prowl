namespace SoftwareRasterizer.Rasterizer.Engines;

internal class LineRasterizer(GraphicsDevice device) : RasterizerBase(device)
{
    public override void Rasterize(RasterTriangle triangle)
    {
        DrawLine(triangle.Vertices[0], triangle.Vertices[1]);
        DrawLine(triangle.Vertices[1], triangle.Vertices[2]);
        DrawLine(triangle.Vertices[2], triangle.Vertices[0]);
    }
}
