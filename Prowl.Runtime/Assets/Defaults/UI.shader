Shader "Paper/UI"
{
    Pass
    {
        Name "UI"
        Tags { "RenderOrder" = "Opaque" }
        Blend One OneMinusSrcAlpha
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        struct VertexInput
        {
            float2 aPosition : POSITION0;
            float2 aTexCoord : TEXCOORD0;
            float4 aColor : COLOR0;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 fragTexCoord : TEXCOORD0;
            float4 fragColor : COLOR0;
            float2 fragPos : TEXCOORD1;
        }

        struct Material
        {
            float4x4 projection;
            float4x4 scissorMat;
            float2 scissorExt;

            float4x4 brushMat;
            int brushType;
            float4 brushColor1;
            float4 brushColor2;
            float4 brushParams;
            float2 brushParams2;

            float4x4 brushTextureMat;
            float dpiScale;

            float2 viewportSize;         // framebuffer size in pixels
            float backdropBlurAmount;  // > 0 when this fill is frosted glass
            int backdropFlipY;         // 1 to flip the backdrop sample vertically

            Sampler2D<float4> texture0;
            Sampler2D<float4> backdropTexture; // blurred copy of the scene behind the shape
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.fragTexCoord = input.aTexCoord;
            o.fragColor = input.aColor;
            o.fragPos = input.aPosition;
            o.position = mul(Mat.projection, float4(input.aPosition, 0.0, 1.0));
            return o;
        }

        // ============== Canvas functions ==============

        float calculateBrushFactor(float2 fragPos) {
            if (Mat.brushType == 0) return 0.0;
            float2 logicalPos = fragPos / max(Mat.dpiScale, 0.001);
            float2 transformedPoint = mul(Mat.brushMat, float4(logicalPos, 0.0, 1.0)).xy;

            if (Mat.brushType == 1) {
                float2 startPoint = Mat.brushParams.xy; float2 endPoint = Mat.brushParams.zw;
                float2 line = endPoint - startPoint; float lineLength = length(line);
                if (lineLength < 0.001) return 0.0;
                return clamp(dot(transformedPoint - startPoint, line) / (lineLength * lineLength), 0.0, 1.0);
            }
            if (Mat.brushType == 2) {
                float2 center = Mat.brushParams.xy;
                return clamp(smoothstep(Mat.brushParams.z, Mat.brushParams.w, length(transformedPoint - center)), 0.0, 1.0);
            }
            if (Mat.brushType == 3) {
                float2 center = Mat.brushParams.xy; float2 halfSize = Mat.brushParams.zw;
                float radius = Mat.brushParams2.x; float feather = Mat.brushParams2.y;
                if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
                float2 q = abs(transformedPoint - center) - (halfSize - float2(radius));
                float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;
                return clamp((dist + feather * 0.5) / feather, 0.0, 1.0);
            }
            return 0.0;
        }

        float scissorMask(float2 p) {
            if(Mat.scissorExt.x < 0.0 || Mat.scissorExt.y < 0.0) return 1.0;
            float dpi = max(Mat.dpiScale, 0.001);
            float2 logicalP = p / dpi;
            float2 transformedPoint = mul(Mat.scissorMat, float4(logicalP, 0.0, 1.0)).xy;
            float2 logicalExt = Mat.scissorExt / dpi;
            float2 distanceFromEdges = abs(transformedPoint) - logicalExt;
            float halfPixelLogical = 0.5 / dpi;
            float2 smoothEdges = float2(halfPixelLogical) - distanceFromEdges;
            return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float mask = scissorMask(input.fragPos);
            float4 color = input.fragColor;

            if (Mat.brushType > 0) {
                float factor = calculateBrushFactor(input.fragPos);
                color = lerp(Mat.brushColor1, Mat.brushColor2, factor);
            }

            // Bitmap text mode: UV.x >= 2.0
            if (input.fragTexCoord.x >= 2.0) {
                return color * Mat.texture0.Sample(input.fragTexCoord - float2(2.0)) * mask;
            }

            // Standard canvas rendering with edge AA
            float2 pixelSize = fwidth(input.fragTexCoord);
            float2 edgeDistance = min(input.fragTexCoord, 1.0 - input.fragTexCoord);
            float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
            edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);

            float dpi = max(Mat.dpiScale, 0.001);
            float2 logicalPos = input.fragPos / dpi;
            float4 fill = color * Mat.texture0.Sample(mul(Mat.brushTextureMat, float4(logicalPos, 0.0, 1.0)).xy);

            // Backdrop blur: composite the fill over the blurred scene behind the shape.
            if (Mat.backdropBlurAmount > 0.0) {
                float2 uv = input.fragPos / Mat.viewportSize;
                if (Mat.backdropFlipY == 1) uv.y = 1.0 - uv.y;
                float3 blurred = Mat.backdropTexture.Sample(uv).rgb;
                float3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;  // fill is premultiplied
                return float4(outRgb, 1.0) * edgeAlpha * mask;
            }

            return fill * edgeAlpha * mask;
        }

        ENDSLANG
    }

    Pass
    {
        Name "BlurDown"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _Offset;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.uv = input.uv;
            o.position = float4(input.position, 1.0);
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            uint texW, texH;
            Mat._MainTex.GetDimensions(texW, texH);
            float2 halfpixel = (0.5 / float2(texW, texH)) * Mat._Offset;

            float4 sum = Mat._MainTex.Sample(input.uv) * 4.0;
            sum += Mat._MainTex.Sample(input.uv - halfpixel);
            sum += Mat._MainTex.Sample(input.uv + halfpixel);
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x, -halfpixel.y));
            sum += Mat._MainTex.Sample(input.uv - float2(halfpixel.x, -halfpixel.y));

            return sum / 8.0;
        }

        ENDSLANG
    }

    Pass
    {
        Name "BlurUp"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _Offset;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.uv = input.uv;
            o.position = float4(input.position, 1.0);
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            uint texW, texH;
            Mat._MainTex.GetDimensions(texW, texH);
            float2 halfpixel = (0.5 / float2(texW, texH)) * Mat._Offset;

            float4 sum = Mat._MainTex.Sample(input.uv + float2(-halfpixel.x * 2.0, 0.0));
            sum += Mat._MainTex.Sample(input.uv + float2(-halfpixel.x, halfpixel.y)) * 2.0;
            sum += Mat._MainTex.Sample(input.uv + float2(0.0, halfpixel.y * 2.0));
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x, halfpixel.y)) * 2.0;
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x * 2.0, 0.0));
            sum += Mat._MainTex.Sample(input.uv + float2(halfpixel.x, -halfpixel.y)) * 2.0;
            sum += Mat._MainTex.Sample(input.uv + float2(0.0, -halfpixel.y * 2.0));
            sum += Mat._MainTex.Sample(input.uv + float2(-halfpixel.x, -halfpixel.y)) * 2.0;

            return sum / 12.0;
        }

        ENDSLANG
    }
}
