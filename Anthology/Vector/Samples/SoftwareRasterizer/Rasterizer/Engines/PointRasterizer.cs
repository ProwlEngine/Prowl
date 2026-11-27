namespace SoftwareRasterizer.Rasterizer.Engines;

internal class PointRasterizer(GraphicsDevice device) : RasterizerBase(device)
{
    public override void Rasterize(RasterTriangle triangle)
    {
        DrawPoint(triangle.Vertices[0]);
        DrawPoint(triangle.Vertices[1]);
        DrawPoint(triangle.Vertices[2]);
    }

    public void RasterizePoint(RasterVertex vertex)
    {
        DrawPoint(vertex);
    }
}
