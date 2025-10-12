// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using SoftwareRasterizer.Rasterizer.Engines;

using Float3 = Prowl.Vector.Float3;

namespace SoftwareRasterizer;

public class DiffuseShader : Rasterizer.Shader
{
    public Float4x4 modelMatrix;
    public Float4x4 viewMatrix;
    public Float4x4 projectionMatrix;
    public float alpha = 1.0f;
    public Float3 lightDir;

    public override bool CanWriteDepth => false;

    // Pre-computed matrices
    private Float4x4 s_mvpMatrix;        // Combined model-view-projection
    private Float4x4 s_modelViewMatrix;  // Combined model-view
    private Float3x3 s_normalMatrix;

    public override void Prepare()
    {
        // Pre-compute combined matrices once
        var modelView = viewMatrix * modelMatrix;
        s_mvpMatrix = projectionMatrix * modelView;
        s_modelViewMatrix = modelView;

        Float3x3 upper3x3Model = new Float3x3(modelMatrix.c0.XYZ, modelMatrix.c1.XYZ, modelMatrix.c2.XYZ);
        s_normalMatrix = Float3x3.Transpose(upper3x3Model.Invert());
    }

    public override ShaderOutput VertexShader(int vertexIndex)
    {
        var vPosition = GetVertexAttribute<Float3>(vertexIndex, 0);
        var vNormal = GetVertexAttribute<Float3>(vertexIndex, 1);

        Float4 clipPos = s_mvpMatrix * new Float4(vPosition, 1.0f);

        // Transform normal to world space
        Float3 transformedNormal = (s_normalMatrix * vNormal);

        return new ShaderOutput
        {
            GlPosition = clipPos,
            Varyings = [ (Float4)vPosition, (Float4)transformedNormal ] // 0: position, 1: normal
        };
    }

    public override FragmentOutput FragmentShader(Float4[] varyings, FragmentContext context)
    {
        var position = varyings[0].XYZ;
        var normal = varyings[1].XYZ;

        float diffuse = Maths.Max(0, Float3.Dot(normal, -lightDir));

        return new FragmentOutput
        {
            GlFragColor = new Float4(
                (((normal.X * 0.5f) + 0.5f) + diffuse) * 0.5f,
                (((normal.Y * 0.5f) + 0.5f) + diffuse) * 0.5f,
                (((normal.Z * 0.5f) + 0.5f) + diffuse) * 0.5f,
                alpha
            )
        };
    }
}
