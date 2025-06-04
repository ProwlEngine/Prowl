// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using SoftwareRasterizer.Rasterizer.Engines;

using Float3 = Prowl.Vector.Float3;

namespace SoftwareRasterizer;

public class DiffuseShader : Rasterizer.Shader
{
    [VertexInput(0)]
    public Float3 vPosition { get; set; }

    [VertexInput(1)]
    public Float3 vNormal { get; set; }

    public Float4x4 modelMatrix;
    public Float4x4 viewMatrix;
    public Float4x4 projectionMatrix;
    public float alpha = 1.0f;
    public Float3 lightDir;

    public override bool CanWriteDepth => false;

    public override ShaderOutput VertexShader()
    {
        // First transform the position by model matrix
        Float4 worldPos = Maths.Mul(modelMatrix, new Float4(vPosition, 1.0f));

        // Then by view matrix
        Float4 viewPos = Maths.Mul(viewMatrix, worldPos);

        // Finally by projection matrix
        Float4 clipPos = Maths.Mul(projectionMatrix, viewPos);

        // Transform normal to world space
        Float3x3 upper3x3Model = new Float3x3(modelMatrix.c0.XYZ, modelMatrix.c1.XYZ, modelMatrix.c2.XYZ);
        Float3x3 normalMatrix = Maths.Transpose(Maths.Inverse(upper3x3Model));
        Float3 transformedNormal = Maths.Normalize(Maths.Mul(normalMatrix, vNormal));

        return new ShaderOutput
        {
            GlPosition = clipPos,
            Varyings = [ vPosition, transformedNormal ] // 0: position, 1: normal
        };
    }

    public override FragmentOutput FragmentShader(Float4[] varyings, FragmentContext context)
    {
        var position = varyings[0].XYZ;
        var normal = varyings[1].XYZ;

        // Normalize position values to range from 0 to 1
        var normalizedPosition = new Float3(
            (position.X + 1) / 2,
            (position.Y + 1) / 2,
            (position.Z + 1) / 2
        );

        float diffuse = Maths.Max(0, Maths.Dot(normal, -lightDir));

        return new FragmentOutput
        {
            GlFragColor = new Float4(
                (normalizedPosition.X + diffuse) * 0.5f,
                (normalizedPosition.Y + diffuse) * 0.5f,
                (normalizedPosition.Z + diffuse) * 0.5f,
                alpha
            )
        };
    }
}
