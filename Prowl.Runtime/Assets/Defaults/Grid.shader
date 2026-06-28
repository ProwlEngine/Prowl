Shader "Default/Grid"
{
    Properties
    {
        _GridColor("Grid Color", Color) = (0.5, 0.5, 0.5, 0.3)
        _PrimaryGridSize("Primary Grid Size", Float) = 1.0
        _SecondaryGridSize("Secondary Grid Size", Float) = 0.25
        _LineWidth("Line Width", Float) = 0.02
        _Falloff("Falloff", Float) = 1.5
        _MaxDist("Max Distance", Float) = 500.0
    }

    Pass
    {
        Name "Grid"
        Tags { "RenderOrder" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LessEqual
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; }
        struct Varyings
        {
            float4 position : SV_Position;
            float3 worldPos : TEXCOORD0;
            float3 viewPos : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float4 clipPos : TEXCOORD3;
        }

        struct Material
        {
            float4 _GridColor;
            float _PrimaryGridSize;
            float _SecondaryGridSize;
            float _LineWidth;
            float _Falloff;
            float _MaxDist;
            Sampler2D<float4> _CameraDepthTexture;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            float4 wp = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0));
            output.worldPos = wp.xyz;
            output.viewPos = mul(Frame.prowl_MatV, wp).xyz;
            output.uv = wp.xz;
            output.clipPos = mul(Object.mvp, float4(input.position, 1.0));
            output.position = output.clipPos;
            return output;
        }

        // Pristine grid anti-aliased grid lines using screen-space derivatives
        // https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8
        float pristineGrid(float2 uv, float2 lineWidth)
        {
            lineWidth = clamp(lineWidth, float2(0.0), float2(0.5));

            float4 uvDDXY = float4(ddx(uv), ddy(uv));
            float2 uvDeriv = float2(length(uvDDXY.xz), length(uvDDXY.yw));

            bool2 invertLine = lineWidth > float2(0.5);

            float2 targetWidth = float2(
                invertLine.x ? 1.0 - lineWidth.x : lineWidth.x,
                invertLine.y ? 1.0 - lineWidth.y : lineWidth.y
            );

            float2 drawWidth = clamp(targetWidth, uvDeriv, float2(0.5));
            float2 lineAA = max(uvDeriv, float2(0.000001)) * 1.5;
            float2 gridUV = abs(frac(uv) * 2.0 - 1.0);

            gridUV.x = invertLine.x ? gridUV.x : 1.0 - gridUV.x;
            gridUV.y = invertLine.y ? gridUV.y : 1.0 - gridUV.y;

            float2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);
            grid2 *= clamp(targetWidth / drawWidth, float2(0.0), float2(1.0));
            grid2 = lerp(grid2, targetWidth, clamp(uvDeriv * 2.0 - 1.0, float2(0.0), float2(1.0)));

            grid2.x = invertLine.x ? 1.0 - grid2.x : grid2.x;
            grid2.y = invertLine.y ? 1.0 - grid2.y : grid2.y;

            return lerp(grid2.x, 1.0, grid2.y);
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            // Sample scene depth and compare with grid fragment depth
            float2 screenUV = input.position.xy / Frame._ScreenParams.xy;
            float sceneDepthRaw = Mat._CameraDepthTexture.Sample(screenUV).r;
            float sceneDepthLinear = linearizeDepthFromProjection(sceneDepthRaw);
            float gridDepthLinear = linearizeDepthFromProjection(input.position.z);

            // Discard if scene geometry is at nearly the same depth (z-fight zone)
            float depthDiff = abs(sceneDepthLinear - gridDepthLinear);
            float threshold = gridDepthLinear * 0.005; // 0.5% of depth scales with distance
            if (depthDiff < threshold)
                discard;

            // Primary (small) and secondary (large) grids
            float sg = pristineGrid(input.uv * Mat._PrimaryGridSize, float2(Mat._LineWidth));
            float bg = pristineGrid(input.uv * Mat._SecondaryGridSize, float2(Mat._LineWidth));

            float gridAlpha = max(sg, bg);

            // Axis highlights constant screen-width lines
            float3 color = Mat._GridColor.rgb;

            float dzPerPx = length(float2(ddx(input.uv.y), ddy(input.uv.y)));
            float dxPerPx = length(float2(ddx(input.uv.x), ddy(input.uv.x)));

            // X axis (red, runs along X where Z ~= 0)
            float xAxis = 1.0 - smoothstep(0.0, dzPerPx * 1.5, abs(input.uv.y));
            // Z axis (blue, runs along Z where X ~= 0)
            float zAxis = 1.0 - smoothstep(0.0, dxPerPx * 1.5, abs(input.uv.x));

            color = lerp(color, float3(0.9, 0.2, 0.2), xAxis);
            gridAlpha = max(gridAlpha, xAxis * 0.9);
            color = lerp(color, float3(0.2, 0.4, 0.9), zAxis);
            gridAlpha = max(gridAlpha, zAxis * 0.9);

            // Distance fade
            float dist = length(input.viewPos);
            float fade = 1.0 - pow(clamp(dist / Mat._MaxDist, 0.0, 1.0), Mat._Falloff);

            return float4(color, gridAlpha * Mat._GridColor.a * fade);
        }

        ENDSLANG
    }
}
