Shader "Default/FXAA"
{
    Pass
    {
        Name "FXAA"
        Tags { "RenderOrder" = "Opaque" }

        Blend SourceAlpha InverseSourceAlpha
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float2 _Resolution;
            float _EdgeThresholdMin;
            float _EdgeThresholdMax;
            float _SubpixelQuality;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        }

        float GetLuminance(float3 color) {
            return dot(color, float3(0.299, 0.587, 0.114));
        }

        float3 FXAA311(float2 texCoord) {
            float quality[12] = { 1.0, 1.0, 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0 };
            int iterations = 12;

            float2 inverseScreenSize = 1.0 / Mat._Resolution;
            int2 texelCoord = int2(texCoord * Mat._Resolution);

            float3 colorCenter = Mat._MainTex.Load(int3(texelCoord, 0)).rgb;

            float lumaCenter = GetLuminance(colorCenter);
            float lumaDown   = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2( 0, -1), 0)).rgb);
            float lumaUp     = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2( 0,  1), 0)).rgb);
            float lumaLeft   = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2(-1,  0), 0)).rgb);
            float lumaRight  = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2( 1,  0), 0)).rgb);

            float lumaMin = min(lumaCenter, min(min(lumaDown, lumaUp), min(lumaLeft, lumaRight)));
            float lumaMax = max(lumaCenter, max(max(lumaDown, lumaUp), max(lumaLeft, lumaRight)));

            float lumaRange = lumaMax - lumaMin;

            if (lumaRange < max(Mat._EdgeThresholdMin, lumaMax * Mat._EdgeThresholdMax)) {
                return colorCenter;
            }

            float lumaDownLeft  = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2(-1, -1), 0)).rgb);
            float lumaUpRight   = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2( 1,  1), 0)).rgb);
            float lumaUpLeft    = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2(-1,  1), 0)).rgb);
            float lumaDownRight = GetLuminance(Mat._MainTex.Load(int3(texelCoord + int2( 1, -1), 0)).rgb);

            float lumaDownUp    = lumaDown + lumaUp;
            float lumaLeftRight = lumaLeft + lumaRight;

            float lumaLeftCorners  = lumaDownLeft  + lumaUpLeft;
            float lumaDownCorners  = lumaDownLeft  + lumaDownRight;
            float lumaRightCorners = lumaDownRight + lumaUpRight;
            float lumaUpCorners    = lumaUpRight   + lumaUpLeft;

            float edgeHorizontal = abs(-2.0 * lumaLeft   + lumaLeftCorners ) +
                                   abs(-2.0 * lumaCenter + lumaDownUp      ) * 2.0 +
                                   abs(-2.0 * lumaRight  + lumaRightCorners);
            float edgeVertical   = abs(-2.0 * lumaUp     + lumaUpCorners   ) +
                                   abs(-2.0 * lumaCenter + lumaLeftRight   ) * 2.0 +
                                   abs(-2.0 * lumaDown   + lumaDownCorners );

            bool isHorizontal = (edgeHorizontal >= edgeVertical);

            float luma1 = isHorizontal ? lumaDown : lumaLeft;
            float luma2 = isHorizontal ? lumaUp : lumaRight;
            float gradient1 = luma1 - lumaCenter;
            float gradient2 = luma2 - lumaCenter;

            bool is1Steepest = abs(gradient1) >= abs(gradient2);
            float gradientScaled = 0.25 * max(abs(gradient1), abs(gradient2));

            float stepLength = isHorizontal ? inverseScreenSize.y : inverseScreenSize.x;

            float lumaLocalAverage = 0.0;

            if (is1Steepest) {
                stepLength = -stepLength;
                lumaLocalAverage = 0.5 * (luma1 + lumaCenter);
            } else {
                lumaLocalAverage = 0.5 * (luma2 + lumaCenter);
            }

            float2 currentUv = texCoord;
            if (isHorizontal) {
                currentUv.y += stepLength * 0.5;
            } else {
                currentUv.x += stepLength * 0.5;
            }

            float2 offset = isHorizontal ? float2(inverseScreenSize.x, 0.0) : float2(0.0, inverseScreenSize.y);

            float2 uv1 = currentUv - offset;
            float2 uv2 = currentUv + offset;

            float lumaEnd1 = GetLuminance(Mat._MainTex.Sample(uv1).rgb);
            float lumaEnd2 = GetLuminance(Mat._MainTex.Sample(uv2).rgb);
            lumaEnd1 -= lumaLocalAverage;
            lumaEnd2 -= lumaLocalAverage;

            bool reached1 = abs(lumaEnd1) >= gradientScaled;
            bool reached2 = abs(lumaEnd2) >= gradientScaled;
            bool reachedBoth = reached1 && reached2;

            if (!reached1) {
                uv1 -= offset;
            }
            if (!reached2) {
                uv2 += offset;
            }

            if (!reachedBoth) {
                for (int i = 2; i < iterations; i++) {
                    if (!reached1) {
                        lumaEnd1 = GetLuminance(Mat._MainTex.Sample(uv1).rgb);
                        lumaEnd1 = lumaEnd1 - lumaLocalAverage;
                    }
                    if (!reached2) {
                        lumaEnd2 = GetLuminance(Mat._MainTex.Sample(uv2).rgb);
                        lumaEnd2 = lumaEnd2 - lumaLocalAverage;
                    }

                    reached1 = abs(lumaEnd1) >= gradientScaled;
                    reached2 = abs(lumaEnd2) >= gradientScaled;
                    reachedBoth = reached1 && reached2;

                    if (!reached1) {
                        uv1 -= offset * quality[i];
                    }
                    if (!reached2) {
                        uv2 += offset * quality[i];
                    }

                    if (reachedBoth) break;
                }
            }

            float distance1 = isHorizontal ? (texCoord.x - uv1.x) : (texCoord.y - uv1.y);
            float distance2 = isHorizontal ? (uv2.x - texCoord.x) : (uv2.y - texCoord.y);

            bool isDirection1 = distance1 < distance2;
            float distanceFinal = min(distance1, distance2);

            float edgeThickness = (distance1 + distance2);

            float pixelOffset = -distanceFinal / edgeThickness + 0.5;

            bool isLumaCenterSmaller = lumaCenter < lumaLocalAverage;

            bool correctVariation = ((isDirection1 ? lumaEnd1 : lumaEnd2) < 0.0) != isLumaCenterSmaller;

            float finalOffset = correctVariation ? pixelOffset : 0.0;

            float lumaAverage = (1.0 / 12.0) * (2.0 * (lumaDownUp + lumaLeftRight) + lumaLeftCorners + lumaRightCorners);
            float subPixelOffset1 = clamp(abs(lumaAverage - lumaCenter) / lumaRange, 0.0, 1.0);
            float subPixelOffset2 = (-2.0 * subPixelOffset1 + 3.0) * subPixelOffset1 * subPixelOffset1;
            float subPixelOffsetFinal = subPixelOffset2 * subPixelOffset2 * Mat._SubpixelQuality;

            finalOffset = max(finalOffset, subPixelOffsetFinal);

            float2 finalUv = texCoord;
            if (isHorizontal) {
                finalUv.y += finalOffset * stepLength;
            } else {
                finalUv.x += finalOffset * stepLength;
            }

            return Mat._MainTex.Sample(finalUv).rgb;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 base = Mat._MainTex.Sample(input.uv);
            float3 color = FXAA311(input.uv);
            return float4(color, base.a);
        }
        ENDSLANG
    }
}
