Shader "Default/FXAA"

Properties
{
}

Pass "FXAA"
{
    Tags { "RenderOrder" = "Opaque" }

    Cull None
    ZTest Off
    ZWrite Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        #include "Fragment"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform vec2 _Resolution;
        uniform float _EdgeThresholdMin;
        uniform float _EdgeThresholdMax;
        uniform float _SubpixelQuality;

        float GetLuminance(vec3 color) {
            return dot(color, vec3(0.299, 0.587, 0.114));
        }

        vec3 FXAA311(vec2 texCoord) {
            float quality[12] = float[12](1.0, 1.0, 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0);
            int iterations = 12;

            vec2 inverseScreenSize = 1.0 / _Resolution;
            ivec2 texelCoord = ivec2(texCoord * _Resolution);

            vec3 colorCenter = texelFetch(_MainTex, texelCoord, 0).rgb;

            float lumaCenter = GetLuminance(colorCenter);
            float lumaDown   = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 0, -1)).rgb);
            float lumaUp     = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 0,  1)).rgb);
            float lumaLeft   = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2(-1,  0)).rgb);
            float lumaRight  = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 1,  0)).rgb);

            float lumaMin = min(lumaCenter, min(min(lumaDown, lumaUp), min(lumaLeft, lumaRight)));
            float lumaMax = max(lumaCenter, max(max(lumaDown, lumaUp), max(lumaLeft, lumaRight)));

            float lumaRange = lumaMax - lumaMin;

            // Early exit if no edge detected
            if (lumaRange < max(_EdgeThresholdMin, lumaMax * _EdgeThresholdMax)) {
                return colorCenter;
            }

            // Sample corners
            float lumaDownLeft  = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2(-1, -1)).rgb);
            float lumaUpRight   = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 1,  1)).rgb);
            float lumaUpLeft    = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2(-1,  1)).rgb);
            float lumaDownRight = GetLuminance(texelFetchOffset(_MainTex, texelCoord, 0, ivec2( 1, -1)).rgb);

            float lumaDownUp    = lumaDown + lumaUp;
            float lumaLeftRight = lumaLeft + lumaRight;

            float lumaLeftCorners  = lumaDownLeft  + lumaUpLeft;
            float lumaDownCorners  = lumaDownLeft  + lumaDownRight;
            float lumaRightCorners = lumaDownRight + lumaUpRight;
            float lumaUpCorners    = lumaUpRight   + lumaUpLeft;

            // Detect horizontal/vertical edge
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

            vec2 currentUv = texCoord;
            if (isHorizontal) {
                currentUv.y += stepLength * 0.5;
            } else {
                currentUv.x += stepLength * 0.5;
            }

            vec2 offset = isHorizontal ? vec2(inverseScreenSize.x, 0.0) : vec2(0.0, inverseScreenSize.y);

            vec2 uv1 = currentUv - offset;
            vec2 uv2 = currentUv + offset;

            float lumaEnd1 = GetLuminance(texture(_MainTex, uv1).rgb);
            float lumaEnd2 = GetLuminance(texture(_MainTex, uv2).rgb);
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
                        lumaEnd1 = GetLuminance(texture(_MainTex, uv1).rgb);
                        lumaEnd1 = lumaEnd1 - lumaLocalAverage;
                    }
                    if (!reached2) {
                        lumaEnd2 = GetLuminance(texture(_MainTex, uv2).rgb);
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

            // Sub-pixel antialiasing
            float lumaAverage = (1.0 / 12.0) * (2.0 * (lumaDownUp + lumaLeftRight) + lumaLeftCorners + lumaRightCorners);
            float subPixelOffset1 = clamp(abs(lumaAverage - lumaCenter) / lumaRange, 0.0, 1.0);
            float subPixelOffset2 = (-2.0 * subPixelOffset1 + 3.0) * subPixelOffset1 * subPixelOffset1;
            float subPixelOffsetFinal = subPixelOffset2 * subPixelOffset2 * _SubpixelQuality;

            finalOffset = max(finalOffset, subPixelOffsetFinal);

            // Compute final UV coordinates
            vec2 finalUv = texCoord;
            if (isHorizontal) {
                finalUv.y += finalOffset * stepLength;
            } else {
                finalUv.x += finalOffset * stepLength;
            }

            return texture(_MainTex, finalUv).rgb;
        }

        void main()
        {
            vec3 color = FXAA311(TexCoords);
            OutputColor = vec4(color, 1.0);
        }
    }

    ENDGLSL
}
